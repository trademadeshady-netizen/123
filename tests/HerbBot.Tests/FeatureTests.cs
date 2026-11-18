using HerbBot.Data;
using HerbBot.Infrastructure;
using HerbBot.Math;
using HerbBot.Routes;
using HerbBot.Runtime;
using HerbBot.Interfaces;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[CollectionDefinition("Sequential")]
public sealed class SequentialCollection { }

public sealed class FeatureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "herbbot-tests-" + Guid.NewGuid());
    private RouteStore Store => new(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Theory]
    [InlineData("AZSHARA", "HERB")]
    [InlineData("DESOLACE", "MINE")]
    [InlineData("TELDRASSIL", "HERB")]
    [InlineData("SILITHUS", "MINE")]
    [InlineData("BLOODMYST_ISLE", "OPEN")]
    public void RealFragments(string zone, string type)
    {
        var nodes = new GathererImporter().Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", zone + ".lua")));
        var node = Assert.Single(nodes);
        Assert.Equal(zone, node.Zone); Assert.Equal(type, node.Type);
        Assert.InRange(node.X, 0, 1); Assert.Null(node.Z); Assert.Null(node.MapId);
        Assert.Equal("DB:Wowhead", node.Source); Assert.True(node.RawFields.ContainsKey("3"));
    }
    [Fact]
    public void FullFileAndCache()
    {
        var parser = new GathererImporter(); string source = Path.Combine(AppContext.BaseDirectory, "Gatherer.lua");
        string cache = Path.Combine(_dir, "cache.json");
        var first = parser.Load(source, cache, "3.3.5a"); var second = parser.Load(source, cache, "3.3.5a");
        Assert.Equal(26276, first.Nodes.Count); Assert.Equal(first.Nodes.Count, second.Nodes.Count);
        Assert.True(first.Nodes.Select(n => n.Zone).Distinct().Count() > 5);
        Assert.Contains(first.Nodes, n => n.Type == "HERB"); Assert.Contains(first.Nodes, n => n.Type == "MINE");
        Assert.Equal(104, first.Nodes.Count(n => n.EntryKey == "TREASURE_POWERCRYST" && n.Entry == null));
        File.WriteAllText(cache, "bad JSON"); Assert.Equal(first.Nodes.Count, parser.Load(source, cache, "3.3.5a").Nodes.Count);
        Assert.Equal("other", parser.Load(source, cache, "other").GameVersion);
    }
    [Theory]
    [InlineData("x = os.execute('anything')")]
    [InlineData("x = {1 + 2}")]
    [InlineData("x = {1, ")]
    [InlineData("x = {a=1,a=2}")]
    public void RejectsExecutableOrBrokenLua(string source) => Assert.Throws<FormatException>(() => new LuaTableParser().Parse(source));

    [Fact]
    public void RecordingRContinuesAndRoundTrips()
    {
        var game = new FakeGame(); var key = new ConsoleRecordHotkey(); var recorder = new RouteRecorder(game, Store, key);
        recorder.Start("flight"); key.Feed(ConsoleKey.R); recorder.Poll(); recorder.Poll();
        game.World.Player.Position = new(10, 2, 50); key.Feed(ConsoleKey.R); recorder.Poll();
        Assert.True(recorder.IsRecording); Assert.Equal(2, recorder.Count);
        recorder.Stop(); Assert.False(recorder.IsRecording);
        Assert.Contains("flight", Store.List()); var loaded = Store.Load("flight");
        Assert.Equal(2, loaded.Points.Count); Assert.Equal(50, loaded.Points[1].Z);
        Assert.Equal(0, loaded.Points[1].MapId); Assert.True(loaded.Points[1].Timestamp > DateTimeOffset.MinValue);
    }
    [Fact]
    public void NearestUses3DAndMap()
    {
        var route = Route("near", P(0, 0, 100), P(2, 0, 0), P(0, 0, 0) with { MapId = 1 });
        Assert.Equal(1, RouteStore.Nearest(route, P(0, 0, 0)));
        Assert.Throws<InvalidOperationException>(() => RouteStore.Nearest(route, P(0, 0, 0) with { MapId = 9 }));
        Assert.Throws<ArgumentException>(() => Store.Save(Route("../bad", P(1, 0, 0))));
    }
    [Fact]
    public void MenuFlightHarvestResumeRouteAndStop()
    {
        Store.Save(Route("flight", P(100, 0, 20), P(5, 0, 20), P(10, 0, 20)));
        var game = new FakeGame(); game.World.Player.IsFlying = true;
        game.World.Player.Position = new(0, 0, 20);
        var bot = Controller(game, new FakeMaps());
        var menu = new BotMenu(bot, Store, new(game, Store, new ConsoleRecordHotkey()), game, new());
        menu.Select("flight"); menu.Start(); Assert.Equal(1, bot.NextPoint);
        bot.Tick(); Assert.Equal(5, game.World.Player.Position.X);
        game.World.Nodes.Add(new() { Guid = 77, Position = new(6, 0, 20) });
        bot.Tick(); Assert.Equal(1, game.ActionsImpl.Attempts); Assert.Equal(1, bot.NextPoint);
        bot.Tick(); Assert.Equal(2, bot.NextPoint);
        bot.Tick(); bot.Tick(); Assert.True(game.TravelPreparations >= 2); Assert.False(bot.IsRunning); bot.Stop();
        Assert.Contains("\"completed\": true", File.ReadAllText(Path.Combine(_dir, "progress", "flight.json")));
    }
    [Fact]
    public void LoopAndExplicitStopSaveProgress()
    {
        Store.Save(Route("loop", P(0, 0, 0), P(10, 0, 0)));
        var game = new FakeGame(); var bot = Controller(game, new FakeMaps()); bot.Loop = true; bot.Start("loop");
        bot.Tick(); Assert.Equal(1, bot.NextPoint); bot.Tick(); bot.Tick(); Assert.Equal(0, bot.NextPoint);
        bot.RequestStop(); bot.Tick(); Assert.False(bot.IsRunning); Assert.True(game.InputImpl.Stops > 0);
        Assert.True(File.Exists(Path.Combine(_dir, "progress", "loop.json")));
    }
    [Fact]
    public void NoMapsOrPathStopsWithoutMovement()
    {
        Store.Save(Route("blocked", P(10, 0, 0))); var game = new FakeGame();
        var bot = Controller(game, new UnavailableMapProvider("missing"));
        Assert.Throws<MapDataUnavailableException>(() => bot.Start("blocked")); Assert.False(bot.IsRunning);
        bot = Controller(game, new FakeMaps { FailPath = true });
        Assert.Throws<InvalidOperationException>(() => bot.Start("blocked")); Assert.False(bot.IsRunning);
        Assert.Equal(0, game.InputImpl.Moves);
    }
    [Fact]
    public void PollingRUsesEdges()
    {
        var state = new KeyState(); var hotkey = new PollingRecordHotkey(state);
        var recorder = new RouteRecorder(new FakeGame(), Store, hotkey);
        recorder.Start("polling"); state.Down = true; recorder.Poll(); recorder.Poll();
        state.Down = false; recorder.Poll(); state.Down = true; recorder.Poll();
        Assert.True(recorder.IsRecording); Assert.Equal(2, recorder.Count); recorder.Stop();
    }
    [Fact]
    public void EmptyRecordingCanBeStoppedAndRestarted()
    {
        var recorder = new RouteRecorder(new FakeGame(), Store, new ConsoleRecordHotkey());
        recorder.Start("empty"); Assert.Empty(recorder.Stop().Points); Assert.False(recorder.IsRecording);
        Assert.Empty(Store.List()); recorder.Start("again"); Assert.True(recorder.IsRecording);
    }
    private BotController Controller(FakeGame game, IMapProvider maps) => new(game, maps, Store, new(), _ => { });
    private static RoutePoint P(float x, float y, float z) => new(x, y, z, 0, DateTimeOffset.UtcNow);
    private static HerbRoute Route(string name, params RoutePoint[] points) => new(1, name, [.. points]);
}

// Свободный мир существует исключительно в тестовом адаптере; приложение его не использует.
sealed class FakeMaps : IMapProvider
{
    public bool FailPath;
    public void Load(int mapId, string version) { }
    public List<Vector3> FindPath(Vector3 start, Vector3 end, bool flying) => FailPath ? throw new InvalidOperationException("No path") : [start, end];
    public bool HasObstacle(Vector3 a, Vector3 b) => false;
    public RoutePoint ToWorld(GatherNode node) => throw new NotSupportedException();
}
sealed class FakeGame : IGameAdapter
{
    public World World = new(); public FakeInput InputImpl; public Actions ActionsImpl;
    public FakeGame() { InputImpl = new(World); ActionsImpl = new(World) { SuccessOn = 1, RemoveOnSuccess = true }; }
    public int TravelPreparations;
    public void Initialize(string gameVersion) { }
    public void PrepareTravel(bool flying) { TravelPreparations++; World.Player.IsFlying = flying; }
    public IObjectManager Objects => World; public IGameInput Input => InputImpl; public IGameActions Actions => ActionsImpl;
    public RoutePoint GetPosition() => new(World.Player.Position.X, World.Player.Position.Y, World.Player.Position.Z, 0, DateTimeOffset.UtcNow);
}
sealed class FakeInput(World world) : IGameInput
{
    public int Stops, Moves;
    public void ClickToMove(Vector3 target) { Moves++; world.Player.Position = target; }
    public void Stop() => Stops++;
    public void Jump() { }
    public void MoveBackward(int ms) { }
}

sealed class KeyState : IKeyStateProvider { public bool Down; public bool IsRDown() => Down; }
