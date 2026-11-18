using System.Buffers.Binary;
using HerbBot.Routes;
namespace HerbBot.Memory;

/// <summary>Смещения предоставляются для конкретного исполняемого файла; адреса не угадываются по версии сервера.</summary>
public class MemoryProfile
{
    public HerbBot.Objects.ObjectManagerLayout? ObjectManager { get; set; }
    public AddressChain? Health { get; set; }
    public AddressChain? MaxHealth { get; set; }
    public AddressChain? Speed { get; set; }
    public int? Build { get; set; }
    public int ReadTimeoutMs { get; set; } = 500;
    public AddressChain? PlayerObject { get; set; }
    public AddressChain? Guid { get; set; }
    public AddressChain? Rotation { get; set; }
    public FlagField? IsMounted { get; set; }
    public FlagField? IsFlying { get; set; }
    public FlagField? IsInCombat { get; set; }
    public FlagField? IsGhost { get; set; }
    public Dictionary<string, string> Sources { get; set; } = new();
    public string GameVersion { get; set; } = "3.3.5a";
    public string ClientFileVersion { get; set; } = "";
    public string ClientSha256 { get; set; } = "";
    public AddressChain? X { get; set; }
    public AddressChain? Y { get; set; }
    public AddressChain? Z { get; set; }
    public AddressChain? MapId { get; set; }
    public void Validate()
    {
        if (ReadTimeoutMs is < 1 or > 10000) throw new InvalidOperationException("ReadTimeoutMs должен быть 1..10000.");
        if (GameVersion != "3.3.5a" || string.IsNullOrWhiteSpace(ClientFileVersion) ||
            ClientSha256 is not { Length: 64 } || !ClientSha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Профиль требует 3.3.5a, точную версию файла и SHA-256 клиента.");
        foreach (var chain in new[] { X, Y, Z, MapId })
            if (chain?.RootRva is null || chain.FinalOffset is null || chain.DereferenceOffsets is null || chain.DereferenceOffsets.Length > 16)
                throw new InvalidOperationException("Профиль требует четыре полные цепочки адресов (не более 16 переходов).");
    }
}
/// <summary>Начать с base+RVA; для каждого элемента прочитать указатель uint32 и прибавить смещение; затем добавить FinalOffset.</summary>
public sealed class AddressChain
{
    public bool RelativeToPlayer { get; set; }
    public long? RootRva { get; set; }
    public long[]? DereferenceOffsets { get; set; }
    public long? FinalOffset { get; set; }
}

/// <summary>Интерфейс позволяет проверять чтение и ошибки независимо от Windows и клиента.</summary>
public interface IReadOnlyProcessMemory : IDisposable
{
    ulong ModuleBase { get; }
    void ReadExactly(ulong address, Span<byte> destination);
}

public sealed class MemoryPositionProvider : IPlayerPositionProvider
{
    private readonly IReadOnlyProcessMemory _memory;
    private readonly AddressChain[] _chains;
    private readonly MemoryProfile _profile;
    public MemoryPositionProvider(IReadOnlyProcessMemory memory, MemoryProfile profile)
    {
        profile.Validate(); _memory = memory; _profile = profile;
        _chains = [profile.X!, profile.Y!, profile.Z!, profile.MapId!];
    }
    public RoutePoint GetPosition()
    {
        // Перепроверяем цепочки и mapId, чтобы не записать позицию после смены объекта или карты.
        // Это не атомарный снимок: XYZ могут меняться внутри одного чтения.
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (timer.ElapsedMilliseconds >= _profile.ReadTimeoutMs) throw new TimeoutException("Таймаут чтения позиции.");
            var addresses = _chains.Select(Resolve).ToArray();
            float x = Float(addresses[0]), y = Float(addresses[1]), z = Float(addresses[2]);
            int map = Int32(addresses[3]);
            if (!_chains.Select(Resolve).SequenceEqual(addresses) || Int32(addresses[3]) != map) continue;
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) ||
                MathF.Abs(x) > 100000 || MathF.Abs(y) > 100000 || MathF.Abs(z) > 100000 || map < 0 || map > 100000)
                throw new InvalidOperationException("Недопустимые координаты или mapId: проверьте профиль и состояние загрузки мира.");
            if (timer.ElapsedMilliseconds >= _profile.ReadTimeoutMs) throw new TimeoutException("Таймаут чтения позиции.");
            return new(x, y, z, map, DateTimeOffset.UtcNow);
        }
        throw new InvalidOperationException("Позиция сменилась во время чтения; повторите после загрузки мира.");
    }
    public PlayerSnapshot GetSnapshot()
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var position = GetPosition();
        if (_profile.Guid == null || _profile.Rotation == null) throw new InvalidOperationException("GUID/Rotation не заданы в профиле.");
        var guidAddress = Resolve(_profile.Guid);
        Span<byte> bytes = stackalloc byte[8]; _memory.ReadExactly(guidAddress, bytes);
        ulong guid = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        float rotation = Float(Resolve(_profile.Rotation));
        if (guid == 0 || !float.IsFinite(rotation)) throw new InvalidOperationException("Неверный GUID или Rotation.");
        var snapshot = new PlayerSnapshot(position, guid, rotation, Flag(_profile.IsMounted), Flag(_profile.IsFlying), Flag(_profile.IsInCombat), Flag(_profile.IsGhost));
        _memory.ReadExactly(Resolve(_profile.Guid), bytes);
        if (BinaryPrimitives.ReadUInt64LittleEndian(bytes) != guid || GetPosition().MapId != position.MapId)
            throw new InvalidOperationException("Игрок или карта сменились во время снимка.");
        if (timer.ElapsedMilliseconds >= _profile.ReadTimeoutMs) throw new TimeoutException("Таймаут снимка игрока.");
        return snapshot;
    }
    private bool Flag(FlagField? field)
    {
        if (field?.Address == null || field.Mask == 0) throw new InvalidOperationException("Флаг/маска не заданы в профиле.");
        return (UInt32(Resolve(field.Address)) & field.Mask) != 0;
    }
    public ulong ResolveAddress(AddressChain chain) => Resolve(chain);
    public int ReadInt(AddressChain? chain) => chain == null ? throw new NotSupportedException("Поле состояния отсутствует в профиле.") : Int32(Resolve(chain));
    public float ReadFloat(AddressChain? chain) => chain == null ? throw new NotSupportedException("Поле состояния отсутствует в профиле.") : Float(Resolve(chain));
    private ulong Resolve(AddressChain chain)
    {
        if (chain.RootRva == null || chain.FinalOffset == null || chain.DereferenceOffsets == null || chain.DereferenceOffsets.Length > 16) throw new InvalidOperationException("Неполная цепочка адреса.");
        ulong root = _memory.ModuleBase;
        if (chain.RelativeToPlayer)
        {
            if (_profile.PlayerObject == null || _profile.PlayerObject.RelativeToPlayer) throw new InvalidOperationException("Нет независимой цепочки PlayerObject.");
            root = Resolve(_profile.PlayerObject);
        }
        ulong address = Add(root, chain.RootRva!.Value);
        foreach (long offset in chain.DereferenceOffsets!)
        {
            uint pointer = UInt32(address);
            if (pointer == 0) throw new InvalidOperationException("Нулевой указатель игрока: мир может быть не загружен.");
            address = Add(pointer, offset);
        }
        return Add(address, chain.FinalOffset!.Value);
    }
    private static ulong Add(ulong address, long offset)
    {
        long value;
        try { value = checked((long)address + offset); }
        catch (OverflowException) { throw new InvalidOperationException("Переполнение адреса клиента."); }
        if (value < 0x10000 || value > uint.MaxValue - 3L) throw new InvalidOperationException("Адрес вне диапазона x86.");
        return (ulong)value;
    }
    private uint UInt32(ulong address) { Span<byte> data = stackalloc byte[4]; _memory.ReadExactly(address, data); return BinaryPrimitives.ReadUInt32LittleEndian(data); }
    private int Int32(ulong address) => unchecked((int)UInt32(address));
    private float Float(ulong address) => BitConverter.Int32BitsToSingle(Int32(address));
}

/// <summary>Имя полной схемы клиента; MemoryProfile оставлен для совместимости позиционных профилей.</summary>
public sealed class ClientProfile : MemoryProfile { }
public sealed class FlagField { public AddressChain? Address { get; set; } public uint Mask { get; set; } }
public sealed record PlayerSnapshot(RoutePoint Position, ulong Guid, float Rotation, bool IsMounted, bool IsFlying, bool IsInCombat, bool IsGhost);
