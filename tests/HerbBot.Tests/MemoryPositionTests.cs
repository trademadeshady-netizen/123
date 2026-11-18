using System.Buffers.Binary;
using HerbBot.Memory;
using HerbBot.Routes;
using Xunit;

public sealed class MemoryPositionTests
{
    private static MemoryProfile Profile() => new()
    {
        ClientFileVersion = "3.3.5.12340", ClientSha256 = new string('A', 64),
        X = Chain(0x100, 0x10), Y = Chain(0x100, 0x14), Z = Chain(0x100, 0x18),
        MapId = new() { RootRva = 0x200, DereferenceOffsets = [], FinalOffset = 0 }
    };
    private static AddressChain Chain(long root, long field) => new() { RootRva = root, DereferenceOffsets = [0], FinalOffset = field };
    private static FakeMemory Memory()
    {
        var m = new FakeMemory(); m.Write(0x100100, 0x200000); m.Write(0x100200, 571);
        m.Write(0x200010, BitConverter.SingleToInt32Bits(12.5f));
        m.Write(0x200014, BitConverter.SingleToInt32Bits(-4.25f));
        m.Write(0x200018, BitConverter.SingleToInt32Bits(30.0f));
        return m;
    }
    [Fact]
    public void ReadsPointerChainAndRecordsRoute()
    {
        var m = Memory(); var provider = new MemoryPositionProvider(m, Profile());
        var p = provider.GetPosition(); Assert.Equal(12.5f, p.X); Assert.Equal(-4.25f, p.Y);
        Assert.Equal(30f, p.Z); Assert.Equal(571, p.MapId); Assert.True(p.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-1));
        var dir = Path.Combine(Path.GetTempPath(), "herb-position-" + Guid.NewGuid());
        try
        {
            var store = new RouteStore(dir); var key = new HerbBot.Runtime.ConsoleRecordHotkey();
            var recorder = new RouteRecorder(provider, store, key); recorder.Start("memory");
            key.Feed(ConsoleKey.R); recorder.Poll(); recorder.Stop();
            Assert.Equal(p.X, store.Load("memory").Points[0].X); Assert.Equal(571, store.Load("memory").Points[0].MapId);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public void RejectsProfileAndInvalidValues()
    {
        var bad = Profile(); bad.ClientSha256 = "";
        Assert.Throws<InvalidOperationException>(() => new MemoryPositionProvider(Memory(), bad));
        bad = Profile(); bad.X!.DereferenceOffsets = null;
        Assert.Throws<InvalidOperationException>(() => new MemoryPositionProvider(Memory(), bad));
        var m = Memory(); m.Write(0x100100, 0);
        Assert.Throws<InvalidOperationException>(() => new MemoryPositionProvider(m, Profile()).GetPosition());
        m = Memory(); m.Write(0x200010, BitConverter.SingleToInt32Bits(float.NaN));
        Assert.Throws<InvalidOperationException>(() => new MemoryPositionProvider(m, Profile()).GetPosition());
        m = Memory(); m.Write(0x100200, -1);
        Assert.Throws<InvalidOperationException>(() => new MemoryPositionProvider(m, Profile()).GetPosition());
    }
    [Fact]
    public void RejectsShortReadsAndAddressOverflow()
    {
        var m = Memory(); m.Missing = 0x200014;
        Assert.Throws<IOException>(() => new MemoryPositionProvider(m, Profile()).GetPosition());
        var p = Profile(); p.X!.RootRva = long.MaxValue;
        Assert.Throws<InvalidOperationException>(() => new MemoryPositionProvider(Memory(), p).GetPosition());
    }
    [Fact]
    public void RejectsChangingMapOrPointer()
    {
        var m = Memory(); m.ChangeMap = true;
        Assert.Throws<InvalidOperationException>(() => new MemoryPositionProvider(m, Profile()).GetPosition());
        m = Memory(); m.ChangePointer = true;
        Assert.Throws<InvalidOperationException>(() => new MemoryPositionProvider(m, Profile()).GetPosition());
    }
    [Fact]
    public void ReadsFullSnapshotAndFailsAfterDisconnect()
    {
        var p = Profile(); var m = Memory();
        p.Guid = Chain(0x100, 0x30); p.Rotation = Chain(0x100, 0x20);
        p.IsMounted = new() { Address = Chain(0x100, 0x24), Mask = 1 };
        p.IsFlying = new() { Address = Chain(0x100, 0x24), Mask = 2 };
        p.IsInCombat = new() { Address = Chain(0x100, 0x24), Mask = 4 };
        p.IsGhost = new() { Address = Chain(0x100, 0x24), Mask = 8 };
        m.Write(0x200020, BitConverter.SingleToInt32Bits(1.5f)); m.Write(0x200024, 3);
        m.Write(0x200030, 123); m.Write(0x200034, 0);
        var reader = new MemoryPositionProvider(m, p); var snapshot = reader.GetSnapshot();
        Assert.Equal(123UL, snapshot.Guid); Assert.Equal(1.5f, snapshot.Rotation);
        Assert.True(snapshot.IsMounted && snapshot.IsFlying); Assert.False(snapshot.IsInCombat || snapshot.IsGhost);
        m.Dispose(); Assert.Throws<IOException>(() => reader.GetPosition());
    }
    [Fact]
    public void RejectsSlowReading()
    {
        var p = Profile(); p.ReadTimeoutMs = 1; var m = Memory(); m.Delay = true;
        Assert.Throws<TimeoutException>(() => new MemoryPositionProvider(m, p).GetPosition());
    }
    private sealed class FakeMemory : IReadOnlyProcessMemory
    {
        private readonly Dictionary<ulong, int> _values = new();
        public ulong ModuleBase => 0x100000;
        private bool _closed; public bool Delay;
        public ulong Missing; public bool ChangeMap, ChangePointer; private int _mapReads, _pointerReads;
        public void Write(ulong address, int value) => _values[address] = value;
        public void ReadExactly(ulong address, Span<byte> destination)
        {
            if (_closed) throw new IOException("Процесс отключён");
            if (Delay) Thread.Sleep(2);
            if (destination.Length == 8) { ReadExactly(address, destination[..4]); ReadExactly(address + 4, destination[4..]); return; }
            if (address == Missing || !_values.TryGetValue(address, out int value)) throw new IOException("Неполное чтение");
            if (ChangeMap && address == 0x100200) value += ++_mapReads % 2;
            // Повторная проверка прерывается при первом изменении: 3 чтения плюс проверка.
            if (ChangePointer && address == 0x100100 && ++_pointerReads % 4 == 0) value += 4;
            BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        }
        public void Dispose() { _closed = true; }
    }
}
