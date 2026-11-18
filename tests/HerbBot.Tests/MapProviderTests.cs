using System.Buffers.Binary;
using HerbBot.Data;
using HerbBot.Maps;
using HerbBot.Math;
using HerbBot.Routes;
using HerbBot.Runtime;
using Xunit;

public sealed class MapProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "herb-mmaps-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    private string Map => Path.Combine(_dir, "000.mmap");
    private string Tile => Path.Combine(_dir, "0000102.mmtile");
    private void Files()
    {
        Directory.CreateDirectory(_dir);
        byte[] parameters = new byte[28];
        void P(int i, int value) => BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(i), value);
        P(12, BitConverter.SingleToInt32Bits(533.333f)); P(16, BitConverter.SingleToInt32Bits(533.333f)); P(20, 16); P(24, 1024);
        File.WriteAllBytes(Map, parameters);
        // Это синтетические заголовки, не готовый тайл Detour. Backend проверяет остальные секции и индексы.
        byte[] bytes = new byte[20 + 100 + 3 * 12 + 32];
        void I(int i, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i), value);
        I(0, 0x4d4d4150); I(4, 7); I(8, 15); I(12, bytes.Length - 20); bytes[16] = 1;
        I(20, 0x444e4156); I(24, 7); I(28, 1); I(32, 2); I(44, 1); I(48, 3);
        foreach (int n in new[] { 104, 108, 112 }) I(n, BitConverter.SingleToInt32Bits(10));
        File.WriteAllBytes(Tile, bytes);
    }
    [Fact]
    public void ReadsActualHeaderLayoutsAndRequiresNavigationBackend()
    {
        Files(); var maps = new MmapMapProvider(_dir, new()); maps.Load(0, "3.3.5a");
        Assert.NotNull(maps.Data); Assert.Equal(16, maps.Data.Parameters.MaxTiles);
        var tile = Assert.Single(maps.Data.Tiles); Assert.Equal(1, tile.Polygons); Assert.Equal(3, tile.Vertices);
        Assert.Equal(1, tile.X); Assert.Equal(2, tile.Y); Assert.True(tile.UsesLiquids);
        Assert.Throws<NotSupportedException>(() => maps.FindPath(new(0, 0, 0), new(1, 0, 0), true));
        Assert.Throws<NotSupportedException>(() => maps.HasObstacle(new(0, 0, 0), new(1, 0, 0)));
        var backend = new TestMapBackend(); maps = new(_dir, new(), backend); maps.Load(0, "3.3.5a");
        Assert.NotNull(backend.Data); Assert.Equal(2, maps.FindPath(new(0, 0, 0), new(1, 0, 0), false).Count);
    }
    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 999)]
    [InlineData(8, 999)]
    [InlineData(12, 1)]
    [InlineData(24, 999)]
    [InlineData(48, int.MaxValue)]
    public void RejectsCorruptOrWrongVersionTiles(int offset, int value)
    {
        Files(); byte[] bytes = File.ReadAllBytes(Tile); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value); File.WriteAllBytes(Tile, bytes);
        Assert.Throws<InvalidDataException>(() => MmapMapProvider.ReadTile(Tile, new()));
    }
    [Fact]
    public void MissingFilesFailAndWarningDoesNotInventPath()
    {
        var maps = new MmapMapProvider(_dir, new()); var ex = Assert.Throws<MapDataMissingException>(() => maps.Load(0, "3.3.5a"));
        Assert.Contains(Path.GetFullPath(_dir), ex.Message); Assert.Contains("3.3.5a", ex.Message);
        Files(); File.Delete(Tile); Assert.Throws<MapDataMissingException>(() => maps.Load(0, "3.3.5a"));
        var logs = new List<string>(); maps = new(_dir, new() { MissingData = "warn_and_continue" }, log: logs.Add);
        maps.Load(0, "3.3.5a"); Assert.Single(logs);
        Assert.Throws<MapDataMissingException>(() => maps.FindPath(new(0, 0, 0), new(1, 0, 0), true));
    }
    [Fact]
    public void FactoryDefaultsToMmapsAndStraightLineIsTestOnly()
    {
        var config = new BotConfiguration(); Assert.IsType<MmapMapProvider>(MapProviderFactory.Create(config)); Assert.Equal("fail", config.Maps.MissingData);
        config.Maps.Provider = "straight_line_test";
        Assert.Throws<InvalidOperationException>(() => MapProviderFactory.Create(config));
        Assert.Throws<InvalidOperationException>(() => new StraightLineMapProvider(false));
        Assert.IsType<StraightLineMapProvider>(MapProviderFactory.Create(config, true));
        config.Maps.Provider = "unknown"; Assert.Throws<ArgumentException>(() => MapProviderFactory.Create(config));
    }
    [Fact]
    public void TruncatedAndWrongMapParamsAreRejected()
    {
        Files(); File.WriteAllBytes(Map, new byte[27]); Assert.Throws<InvalidDataException>(() => MmapMapProvider.ReadParameters(Map));
        File.WriteAllBytes(Tile, new byte[19]); Assert.Throws<EndOfStreamException>(() => MmapMapProvider.ReadTile(Tile, new()));
    }
}
sealed class TestMapBackend : IMapNavigationBackend
{
    public MmapData? Data;
    public void LoadValidated(MmapData data) => Data = data;
    public void Load(int mapId, string version) { }
    public List<Vector3> FindPath(Vector3 a, Vector3 b, bool flying) => [a, b];
    public bool HasObstacle(Vector3 a, Vector3 b) => false;
    public RoutePoint ToWorld(GatherNode node) => throw new NotSupportedException();
}
