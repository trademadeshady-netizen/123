using System.Buffers.Binary;
using HerbBot.Memory;
using HerbBot.Objects;
using Xunit;

public sealed class ObjectScannerTests
{
    [Fact]
    public void EmptyAndOneAndSeveralWithEntryTypeFlagAndRadiusFilters()
    {
        var f = new ScanFixture(); Assert.Empty(f.Scanner.GetVisibleHerbs(100));
        f.Node(0x200000, 1, 1617, 1); Assert.Equal(1UL, Assert.Single(f.Scanner.GetVisibleHerbs(100)).Guid);
        f.Node(0x200100, 2, 1617, 2); f.Link(0x200000, 0x200100);
        Assert.Equal(new ulong[] { 1, 2 }, f.Scanner.GetVisibleHerbs(100).Select(n => n.Guid));
        f.Memory.Write(0x200110, 9999); Assert.Single(f.Scanner.GetVisibleHerbs(100));
        f.Memory.Write(0x200110, 1617); f.Memory.Write(0x200104, 2); Assert.Single(f.Scanner.GetVisibleHerbs(100));
        f.Memory.Write(0x200104, 5); f.Layout.HerbMask = 2; Assert.Empty(f.Scanner.GetVisibleHerbs(100));
        f.Memory.Write(0x200020, 2); Assert.Single(f.Scanner.GetVisibleHerbs(100));
        f.Layout.HerbMask = null; Assert.Single(f.Scanner.GetVisibleHerbs(1.1f));
    }
    [Fact]
    public void PartialReadAndBadAddressAreErrorsNotEmptyWorld()
    {
        var f = new ScanFixture(); f.Node(0x200000, 1, 1617, 1); f.Memory.Missing = 0x200018;
        Assert.Throws<IOException>(() => f.Scanner.GetVisibleHerbs(100));
        f.Memory.Missing = 0; f.Memory.Write(0x101000, 123);
        Assert.Throws<IOException>(() => f.Scanner.GetVisibleHerbs(100));
    }
    [Fact]
    public void GarbageOrReusedGuidIsSkippedAndCyclesAreRejected()
    {
        var f = new ScanFixture(); f.Node(0x200000, 1, 1617, float.NaN);
        Assert.Empty(f.Scanner.GetVisibleHerbs(100));
        f.Memory.Float(0x200014, 1); f.Memory.ChangeGuid = 0x200008;
        Assert.Empty(f.Scanner.GetVisibleHerbs(100));
        f.Memory.ChangeGuid = 0; f.Link(0x200000, 0x200000);
        Assert.Throws<IOException>(() => f.Scanner.GetVisibleHerbs(100));
    }
    [Fact]
    public void ArrayUsesStrideAndCountAndTimeoutIsExplicit()
    {
        var f = new ScanFixture(); f.Node(0x200000, 1, 1617, 1); f.Node(0x200040, 2, 1617, 2);
        f.Layout.Traversal = "array"; f.Layout.CountOffset = 8; f.Layout.Stride = 64; f.Memory.Write(0x101008, 2);
        Assert.Equal(2, f.Scanner.GetVisibleHerbs(100).Count);
        f.Memory.SlowObject = true; f.Layout.TimeoutMs = 1;
        Assert.Throws<TimeoutException>(() => f.Scanner.GetVisibleHerbs(100));
    }
    [Fact]
    public void DescriptorAndLastSentinelAreProfileDriven()
    {
        var f = new ScanFixture(); f.Node(0x200000, 1, 1617, 1); f.Node(0x200100, 2, 1617, 2); f.Link(0x200000, 0x200100);
        f.Layout.LastOffset = 4; f.Memory.Write(0x101004, 0x200100);
        Assert.Single(f.Scanner.GetVisibleHerbs(100));
        f.Layout.LastInclusive = true; Assert.Equal(2, f.Scanner.GetVisibleHerbs(100).Count);
        f.Layout.RecordSize = 48; f.Layout.Entry = new() { PointerOffsets = [40], Offset = 8 };
        f.Memory.Write(0x200028, 0x300000); f.Memory.Write(0x300008, 1617);
        f.Memory.Write(0x200128, 0x300100); f.Memory.Write(0x300108, 1617);
        Assert.Equal(2, f.Scanner.GetVisibleHerbs(100).Count);
    }
    [Fact]
    public void EndSentinelAtLimitIsValidButTruncatedListsAreNot()
    {
        var f = new ScanFixture(); f.Node(0x200000, 1, 1617, 1);
        f.Layout.MaxObjects = 1; f.Layout.LastOffset = 4; f.Memory.Write(0x101004, 0x200100); f.Link(0x200000, 0x200100);
        Assert.Single(f.Scanner.GetVisibleHerbs(100));
        f.Link(0x200000, 0); Assert.Throws<IOException>(() => f.Scanner.GetVisibleHerbs(100));
        f = new ScanFixture(); f.Layout.Traversal = "array"; f.Layout.Stride = 40; f.Layout.CountOffset = 8;
        f.Memory.Write(0x101008, 2); Assert.Throws<IOException>(() => f.Scanner.GetVisibleHerbs(100));
    }
    [Fact]
    public void MissingLayoutAndChangedHeadAreRejected()
    {
        var f = new ScanFixture(); f.Layout.RecordSize = 4;
        Assert.Throws<InvalidOperationException>(() => f.Scanner.GetVisibleHerbs(100));
        f.Layout.RecordSize = 40; f.Memory.ChangeHead = true;
        Assert.Throws<IOException>(() => f.Scanner.GetVisibleHerbs(100));
    }
}

// Адреса ниже синтетические: они не являются кандидатами смещений Wow.exe.
sealed class ScanFixture
{
    public ByteMemory Memory = new();
    public ClientProfile Profile;
    public MemoryPositionProvider Position;
    public ObjectManagerLayout Layout;
    public MemoryObjectScanner Scanner;
    public static AddressChain Chain(long rva) => new() { RootRva = rva, DereferenceOffsets = [], FinalOffset = 0 };
    public ScanFixture()
    {
        Profile = new() { ClientFileVersion = "test", ClientSha256 = new string('a', 64), X = Chain(0), Y = Chain(4), Z = Chain(8), MapId = Chain(12),
            Guid = Chain(16), Rotation = Chain(24), Health = Chain(32), MaxHealth = Chain(36), Speed = Chain(40) };
        Profile.IsMounted = new() { Address = Chain(28), Mask = 1 }; Profile.IsFlying = new() { Address = Chain(28), Mask = 2 };
        Profile.IsInCombat = new() { Address = Chain(28), Mask = 4 }; Profile.IsGhost = new() { Address = Chain(28), Mask = 8 };
        for (ulong i = 0; i <= 40; i += 4) Memory.Write(0x100000 + i, 0);
        Memory.Write(0x100010, 42); Memory.Write(0x100020, 100); Memory.Write(0x100024, 100); Memory.Float(0x100028, 7);
        Memory.Write(0x101000, 0);
        ObjectField F(long offset) => new() { Offset = offset };
        Layout = new() { Manager = Chain(0x1000), FirstOffset = 0, NextOffset = 0, RecordSize = 40, GameObjectType = 5,
            Guid = F(8), Type = F(4), Entry = F(16), X = F(20), Y = F(24), Z = F(28), Flags = F(32), DynamicFlags = F(36) };
        Profile.ObjectManager = Layout; Position = new(Memory, Profile); Scanner = new(Memory, Position, Layout, new HashSet<int> { 1617 });
    }
    public void Node(ulong address, int guid, int entry, float x)
    {
        if (Memory.Values[0x101000] == 0) Memory.Write(0x101000, (int)address);
        for (ulong i = 0; i < 40; i += 4) Memory.Write(address + i, 0);
        Memory.Write(address + 4, 5); Memory.Write(address + 8, guid); Memory.Write(address + 16, entry); Memory.Float(address + 20, x);
    }
    public void Link(ulong from, int to) => Memory.Write(from, to);
}
sealed class ByteMemory : IReadOnlyProcessMemory
{
    public Dictionary<ulong, int> Values = new();
    public ulong Missing, ChangeGuid; public bool SlowObject, ChangeHead; private int _headReads; private bool _disposed;
    public ulong ModuleBase => 0x100000;
    public void Write(ulong address, int value) => Values[address] = value;
    public void Float(ulong address, float value) => Write(address, BitConverter.SingleToInt32Bits(value));
    public void ReadExactly(ulong address, Span<byte> destination)
    {
        if (_disposed) throw new IOException("Disconnected");
        if (destination.Length == 8) { ReadExactly(address, destination[..4]); ReadExactly(address + 4, destination[4..]); return; }
        if (address == Missing || !Values.TryGetValue(address, out int value)) throw new IOException("Partial read");
        if (SlowObject && address >= 0x200000) Thread.Sleep(2);
        if (address == ChangeGuid) Values[address]++;
        if (ChangeHead && address == 0x101000 && ++_headReads > 1) value += 64;
        BinaryPrimitives.WriteInt32LittleEndian(destination, value);
    }
    public void Dispose() => _disposed = true;
}
