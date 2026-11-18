using System.Buffers.Binary;
using System.Diagnostics;
using HerbBot.Memory;
using HerbBot.Math;
namespace HerbBot.Objects;

/// <summary>Все адреса/layout задаются профилем. Это схема данных, а не подтверждённые адреса клиента.</summary>
public sealed class ObjectManagerLayout
{
    public AddressChain? Manager { get; set; }
    public long? FirstOffset { get; set; }
    public long? LastOffset { get; set; }
    public bool LastInclusive { get; set; }
    public string Traversal { get; set; } = "linked";
    public long? NextOffset { get; set; }
    public long? CountOffset { get; set; }
    public int? Stride { get; set; }
    public int RecordSize { get; set; }
    public int MaxObjects { get; set; } = 10000;
    public int TimeoutMs { get; set; } = 500;
    public uint? GameObjectType { get; set; }
    public string HerbFlagField { get; set; } = "flags";
    public uint? HerbMask { get; set; }
    public ObjectField? Guid { get; set; }
    public ObjectField? Type { get; set; }
    public ObjectField? Entry { get; set; }
    public ObjectField? X { get; set; }
    public ObjectField? Y { get; set; }
    public ObjectField? Z { get; set; }
    public ObjectField? Flags { get; set; }
    public ObjectField? DynamicFlags { get; set; }
}
/// <summary>Внешние descriptor-поля описываются цепочкой, чтобы их layout не попадал в код потребителей.</summary>
public sealed class ObjectField { public long? Offset { get; set; } public long[] PointerOffsets { get; set; } = []; }
/// <summary>Независимый снимок: ссылка на адрес объекта не переживает следующий тик и наружу не передаётся.</summary>
public sealed record WorldObject(ulong Guid, int Entry, uint Type, Vector3 Position, uint Flags, uint DynamicFlags, string? Name = null);
public interface IObjectScanner { IReadOnlyList<WorldObject> GetVisibleHerbs(float radius); }

/// <summary>Обход ограничен временем/числом записей; ошибка чтения никогда не означает свободный путь или пустой мир.</summary>
public sealed class MemoryObjectScanner(IReadOnlyProcessMemory memory, MemoryPositionProvider player, ObjectManagerLayout layout, IReadOnlySet<int> herbEntries) : IObjectScanner
{
    private static ulong Add(ulong p, long offset)
    {
        long a = checked((long)p + offset);
        if (a < 0x10000 || a > uint.MaxValue - 8L) throw new IOException("Адрес объекта вне x86.");
        return (ulong)a;
    }
    private uint U32(ulong address) { Span<byte> b = stackalloc byte[4]; memory.ReadExactly(address, b); return BinaryPrimitives.ReadUInt32LittleEndian(b); }
    private ulong U64(ulong address) { Span<byte> b = stackalloc byte[8]; memory.ReadExactly(address, b); return BinaryPrimitives.ReadUInt64LittleEndian(b); }
    private ulong Address(ulong obj, ObjectField? field)
    {
        if (field?.Offset == null || field.PointerOffsets == null || field.PointerOffsets.Length > 16) throw new InvalidOperationException("Поле объекта не задано.");
        foreach (long offset in field.PointerOffsets) obj = U32(Add(obj, offset));
        return Add(obj, field.Offset.Value);
    }
    private uint Field(ulong obj, ObjectField? field) => U32(Address(obj, field));
    public IReadOnlyList<WorldObject> GetVisibleHerbs(float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (layout.Manager == null || layout.FirstOffset == null || layout.GameObjectType == null || layout.RecordSize <= 0 || layout.MaxObjects is < 1 or > 100000 || layout.TimeoutMs is < 1 or > 10000)
            throw new InvalidOperationException("ObjectManager layout не заполнен.");
        if (layout.Traversal != "linked" && layout.Traversal != "array") throw new InvalidOperationException("Traversal должен быть linked/array.");
        // Поля записи не могут выходить за заявленный размер. Указательные поля описывают внешний descriptor.
        foreach (var (field, size) in new[] { (layout.Guid, 8), (layout.Type, 4), (layout.Entry, 4), (layout.X, 4), (layout.Y, 4), (layout.Z, 4), (layout.Flags, 4), (layout.DynamicFlags, 4) })
        {
            if (field?.Offset is null || field.Offset < 0 || field.PointerOffsets == null || field.PointerOffsets.Length > 16 ||
                (field.PointerOffsets.Length == 0 && field.Offset > layout.RecordSize - size) ||
                (field.PointerOffsets.Length > 0 && (field.PointerOffsets[0] < 0 || field.PointerOffsets[0] > layout.RecordSize - 4)))
                throw new InvalidOperationException("Поле ObjectManager выходит за размер записи или не заполнено.");
        }
        if (layout.HerbFlagField != "flags" && layout.HerbFlagField != "dynamicFlags") throw new InvalidOperationException("HerbFlagField: flags или dynamicFlags.");
        if (layout.HerbMask == 0) throw new InvalidOperationException("HerbMask: null означает фильтр по entry; нулевая маска недопустима.");
        if (layout.Traversal == "linked" && (layout.NextOffset == null || layout.NextOffset < 0 || layout.NextOffset > layout.RecordSize - 4)) throw new InvalidOperationException("Неверный NextOffset.");
        if (layout.Traversal == "array" && (layout.Stride == null || layout.Stride < layout.RecordSize)) throw new InvalidOperationException("Stride должен вмещать запись.");
        var started = Stopwatch.StartNew(); var origin = player.GetPosition();
        ulong manager = player.ResolveAddress(layout.Manager);
        ulong current = U32(Add(manager, layout.FirstOffset.Value));
        ulong first = current;
        ulong last = layout.LastOffset.HasValue ? U32(Add(manager, layout.LastOffset.Value)) : 0;
        uint count = layout.Traversal == "array"
            ? U32(Add(manager, layout.CountOffset ?? throw new InvalidOperationException("CountOffset не задан"))) : (uint)layout.MaxObjects;
        if (count > layout.MaxObjects) throw new IOException("Число объектов превысило лимит.");
        uint visited = 0; bool lastSeen = last == 0;
        var guids = new HashSet<ulong>(); var seen = new HashSet<ulong>(); var result = new List<WorldObject>();
        for (uint i = 0; current != 0 && i < count; i++)
        {
            if (started.ElapsedMilliseconds >= layout.TimeoutMs) throw new TimeoutException("Таймаут ObjectManager");
            if (current == last) { lastSeen = true; if (!layout.LastInclusive) break; }
            visited++;
            Add(current, layout.RecordSize - 1);
            if (!seen.Add(current)) throw new IOException("Цикл в списке объектов");
            bool final = (current == last && layout.LastInclusive) || (layout.Traversal == "array" && i + 1 == count);
            ulong next = final ? 0 : layout.Traversal == "linked"
                ? U32(Add(current, layout.NextOffset ?? throw new InvalidOperationException("NextOffset не задан")))
                : Add(current, layout.Stride is > 0 ? layout.Stride.Value : throw new InvalidOperationException("Stride не задан"));
            uint type = Field(current, layout.Type);
            if (type == layout.GameObjectType)
            {
                ulong guid = U64(Address(current, layout.Guid)); uint rawEntry = Field(current, layout.Entry); int entry = rawEntry <= int.MaxValue ? (int)rawEntry : -1;
                uint flags = Field(current, layout.Flags), dynamicFlags = Field(current, layout.DynamicFlags);
                float x = BitConverter.Int32BitsToSingle((int)Field(current, layout.X));
                float y = BitConverter.Int32BitsToSingle((int)Field(current, layout.Y));
                float z = BitConverter.Int32BitsToSingle((int)Field(current, layout.Z));
                // Мусорный/сменившийся объект пропускается; ошибки чтения всей страницы не маскируются пустым списком.
                if (guid != 0 && float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) && MathF.Abs(x) <= 100000 && MathF.Abs(y) <= 100000 && MathF.Abs(z) <= 100000 &&
                    herbEntries.Contains(entry) && (!layout.HerbMask.HasValue || ((layout.HerbFlagField == "flags" ? flags : dynamicFlags) & layout.HerbMask.Value) != 0) && guid == U64(Address(current, layout.Guid)))
                {
                    var pos = new Vector3(x, y, z);
                    if (origin.Position.DistanceTo(pos) <= radius && guids.Add(guid)) result.Add(new(guid, entry, type, pos, flags, dynamicFlags));
                }
            }
            current = next;

        }
        // Отличаем штатный конец от обрезанного/повреждённого списка.
        if (layout.Traversal == "array" && visited != count) throw new IOException("Массив объектов закончился раньше count.");
        if (layout.Traversal == "linked")
        {
            if (current == last) lastSeen = true;
            if (current != 0 && !(current == last && !layout.LastInclusive)) throw new IOException("Список превысил лимит.");
            if (layout.LastOffset.HasValue && !lastSeen) throw new IOException("Последний объект не достигнут.");
        }
        if (player.ResolveAddress(layout.Manager) != manager || U32(Add(manager, layout.FirstOffset.Value)) != first || player.GetPosition().MapId != origin.MapId)
            throw new IOException("ObjectManager/карта сменились во время чтения; повторите сканирование.");
        if (started.ElapsedMilliseconds >= layout.TimeoutMs) throw new TimeoutException("Таймаут ObjectManager.");
        return result;
    }
}
