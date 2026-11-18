using HerbBot.Interfaces;
using HerbBot.Math;
using HerbBot.Memory;
using HerbBot.Navigation;
using HerbBot.Objects;
using HerbBot.Routes;
using HerbBot.Runtime;
using HerbBot.Maps;
using Xunit;

public sealed class GameAdapterTests
{
    [Fact]
    public void MovementStopTargetAndLootHaveState()
    {
        var game = new StatefulGame(); game.Nodes.Add(new(1, 1617, 5, new(10, 0, 3), 0, 0));
        game.MoveTo(new(10, 0, 3), MoveType.Flying); Assert.True(game.IsFlying()); Assert.True(game.Player.IsMoving);
        game.StopMovement(); Assert.False(game.Player.IsMoving); Assert.Equal(10, game.GetPosition().X);
        game.SetMounted(false); Assert.False(game.IsMounted());
        Assert.True(GameInteraction.Gather(game, 1, TimeSpan.FromSeconds(1), () => true, () => true));
        Assert.Equal(1UL, game.Target); Assert.Equal(1, game.Herbs); Assert.False(game.IsLootWindowOpen()); Assert.Empty(game.Nodes);
    }
    [Fact]
    public void DisappearanceBetweenTargetAndLootAndLootTimeout()
    {
        var game = new StatefulGame { DisappearOnTarget = true }; game.AddHerb();
        Assert.False(GameInteraction.Gather(game, 1, TimeSpan.FromMilliseconds(50), () => true, () => true)); Assert.Equal(0, game.Herbs);
        game = new StatefulGame { DisappearOnInteract = true }; game.AddHerb();
        Assert.False(GameInteraction.Gather(game, 1, TimeSpan.FromMilliseconds(50), () => true, () => true)); Assert.Equal(0, game.Herbs);
        game = new StatefulGame { NeverOpenLoot = true }; game.AddHerb();
        Assert.Throws<TimeoutException>(() => GameInteraction.Gather(game, 1, TimeSpan.FromMilliseconds(50), () => true, () => true));
        Assert.Equal(0, game.Herbs);
    }
    [Fact]
    public void RemovedObjectAfterLootWaitsForInventoryAndStopCancelsWaiting()
    {
        var game = new StatefulGame { LootInventoryDelay = 3 }; game.AddHerb();
        Assert.True(GameInteraction.Gather(game, 1, TimeSpan.FromSeconds(1), () => true, () => true));
        Assert.Equal(1, game.Herbs); Assert.Empty(game.Nodes);
        game = new StatefulGame { NeverOpenLoot = true }; game.AddHerb(); int checks = 0;
        Assert.False(GameInteraction.Gather(game, 1, TimeSpan.FromSeconds(10), () => ++checks < 4, () => true));
        Assert.Equal(0, game.Herbs);
    }
    [Fact]
    public void RealAdapterDelegatesCommandsToExplicitBackend()
    {
        var f = new ScanFixture(); f.Node(0x200000, 1, 1617, 1);
        var commands = new StatefulGame(); commands.AddHerb();
        var game = new MemoryGameAdapter(f.Position, f.Profile, f.Scanner, 100, commands);
        game.PrepareTravel(true); game.MoveTo(new(3, 4, 5), MoveType.Flying);
        Assert.Equal(5, commands.Player.Position.Z); Assert.True(commands.IsFlying());
        game.StopMovement(); Assert.False(commands.Player.IsMoving);
        game.TargetObject(1); game.InteractObject(1); Assert.True(game.IsLootWindowOpen());
        game.LootAll(); Assert.Equal(1, game.GetHerbCount()); game.SetMounted(false); Assert.False(commands.IsMounted());
        // Команда backend не подменяет наблюдения: фактические координаты по-прежнему читаются из памяти.
        Assert.Equal(0, game.GetPosition().Z);
    }
    [Fact]
    public void StuckRecoveryAndStopReset()
    {
        var game = new StatefulGame { BlockMovement = true }; var movement = new MovementManager(game, game, 0.01);
        movement.Go([new(0, 0, 0), new(10, 0, 0)], true); Thread.Sleep(25);
        Assert.True(movement.IsStuck()); Assert.Equal(1, game.Recoveries);
        movement.StopMove(); Assert.False(movement.IsStuck());
    }
    [Fact]
    public void RealAdapterReadsMemoryButDoesNotInventInput()
    {
        var f = new ScanFixture(); f.Node(0x200000, 1, 1617, 1);
        var game = new MemoryGameAdapter(f.Position, f.Profile, f.Scanner, 100);
        game.Initialize("3.3.5a"); Assert.Equal(100, game.GetLocalPlayer().Health); Assert.Single(game.GetWoWGameObjectForFarm());
        Assert.False(game.IsMounted());
        Assert.Throws<NotSupportedException>(() => game.MoveTo(new(1, 0, 0), MoveType.Ground));
        Assert.Throws<NotSupportedException>(() => game.TargetObject(1)); Assert.Throws<NotSupportedException>(() => game.LootAll());
        Assert.Throws<NotSupportedException>(() => game.StopMovement());
        f.Memory.Dispose(); Assert.Throws<IOException>(() => game.GetPosition());
    }
    [Fact]
    public void RecordSelectFlyScanGatherReturnLoopAndStopThroughMenu()
    {
        string dir = Path.Combine(Path.GetTempPath(), "herb-e2e-" + Guid.NewGuid());
        try
        {
            var game = new StatefulGame(); var store = new RouteStore(dir); var key = new ConsoleRecordHotkey();
            var recorder = new RouteRecorder(game, store, key); var config = new BotConfiguration { IgnoreIfPlayerNear = false, LoopRoute = true };
            var log = new List<string>(); var bot = new BotController(game, new StraightLineMapProvider(true), store, config, log.Add);
            var menu = new BotMenu(bot, store, recorder, game, config);
            menu.StartRecording("scan");
            game.Player.Position = new(100, 0, 20); key.Feed(ConsoleKey.R); recorder.Poll();
            game.Player.Position = new(5, 0, 20); key.Feed(ConsoleKey.R); recorder.Poll();
            menu.StopRecording(); menu.Select("scan"); game.Player.Position = new(0, 0, 20);
            menu.Start(); Assert.Equal(1, bot.NextPoint); bot.Tick(); Assert.Equal(5, game.Player.Position.X); Assert.True(game.IsFlying());
            game.Nodes.Add(new(1, 1617, 5, new(6, 0, 20), 0, 0)); bot.Tick();
            Assert.Equal(1, game.Herbs); Assert.Equal("returning", bot.State); Assert.Equal(1, bot.NextPoint); Assert.True(game.Scans > 0);
            bot.Tick(); Assert.Equal(0, bot.NextPoint); Assert.True(bot.IsRunning);
            menu.RequestStop(); bot.Tick(); menu.Stop(); Assert.False(bot.IsRunning); Assert.Empty(log);
            Assert.True(File.Exists(Path.Combine(dir, "progress", "scan.json")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public void DisappearedHerbReturnsToRouteAndNoPathStopsExplicitly()
    {
        string dir = Path.Combine(Path.GetTempPath(), "herb-stop-" + Guid.NewGuid());
        try
        {
            var game = new StatefulGame { DisappearOnTarget = true }; var store = new RouteStore(dir);
            store.Save(new(1, "route", [new(10, 0, 0, 0, DateTimeOffset.UtcNow)]));
            var config = new BotConfiguration { IgnoreIfPlayerNear = false }; var logs = new List<string>();
            var bot = new BotController(game, new StraightLineMapProvider(true), store, config, logs.Add);
            bot.Start("route"); game.AddHerb(); bot.Tick(); Assert.Equal("returning", bot.State); Assert.Equal(0, game.Herbs);
            bot.Tick(); bot.Tick(); Assert.False(bot.IsRunning);
            game = new StatefulGame(); bot = new(game, new FakeMaps { FailPath = true }, store, config, logs.Add);
            Assert.Throws<InvalidOperationException>(() => bot.Start("route")); Assert.False(bot.IsRunning);
            Assert.Equal(0, game.Player.Position.X); Assert.Contains(logs, l => l.Contains("Ошибка старта"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

/// <summary>Только тестовый мир: команды немедленно меняют синтетические координаты и инвентарь.
/// Сканер вызывается тем же IObjectManager, который использует FarmingState.</summary>
sealed class StatefulGame : IGameAdapter, IExplicitLootAdapter, IObjectManager, IGameInput, IGameActions, IObjectScanner, IGameCommandBackend
{
    public LocalPlayer Player = new() { Health = 100, MaxHealth = 100, Speed = 7 };
    public List<WorldObject> Nodes = [];
    public ulong Target; public int Herbs, Scans, Recoveries;
    public bool DisappearOnTarget, DisappearOnInteract, NeverOpenLoot, BlockMovement;
    private bool _loot, _casting;
    public int LootInventoryDelay; private int _inventoryPolls;
    public void AddHerb() => Nodes.Add(new(1, 1617, 5, Player.Position, 0, 0));
    public void Initialize(string gameVersion) { }
    public void PrepareTravel(bool flying) { Player.IsMounted = true; Player.IsFlying = flying; }
    public RoutePoint GetPosition() => new(Player.Position.X, Player.Position.Y, Player.Position.Z, 0, DateTimeOffset.UtcNow);
    public void MoveTo(Vector3 target, MoveType type) { Player.IsMoving = true; Player.IsFlying = type == MoveType.Flying; if (!BlockMovement) Player.Position = target; }
    public void StopMovement() => Player.IsMoving = false;
    public void TargetObject(ulong guid) { if (!IsValid(guid)) throw new IOException("Missing target"); Target = guid; if (DisappearOnTarget) Nodes.Clear(); }
    public void InteractObject(ulong guid) { if (Target != guid || !IsValid(guid)) throw new IOException("Target disappeared"); _casting = true; if (DisappearOnInteract) Nodes.Clear(); else _loot = !NeverOpenLoot; }
    public bool IsLootWindowOpen() => _loot;
    public void LootAll() { if (!_loot || !IsValid(Target)) throw new IOException("Loot disappeared"); if (LootInventoryDelay == 0) Herbs++; else _inventoryPolls = LootInventoryDelay; _loot = false; Nodes.RemoveAll(n => n.Guid == Target); }
    public bool IsMounted() => Player.IsMounted;
    public bool IsFlying() => Player.IsFlying;
    public void SetMounted(bool mounted) { Player.IsMounted = mounted; if (!mounted) Player.IsFlying = false; }
    public IReadOnlyList<WorldObject> GetVisibleHerbs(float radius) { Scans++; return Nodes.Where(n => n.Position.DistanceTo(Player.Position) <= radius).ToArray(); }
    public IObjectManager Objects => this; public IGameInput Input => this; public IGameActions Actions => this;
    public LocalPlayer GetLocalPlayer() => Player;
    public IReadOnlyList<WoWGameObject> GetWoWGameObjectForFarm() => GetVisibleHerbs(100).Select(n => new WoWGameObject { Guid = n.Guid, Entry = n.Entry, Position = n.Position }).ToArray();
    public WoWGameObject? GetWoWGameObjectByName(string name) => GetWoWGameObjectForFarm().FirstOrDefault(n => n.Name == name);
    public WoWGameObject? GetWoWGameObjectByEntry(int id) => GetWoWGameObjectForFarm().FirstOrDefault(n => n.Entry == id);
    public bool IsValid(ulong guid) => GetVisibleHerbs(100).Any(n => n.Guid == guid);
    public bool IsOtherPlayerNear(Vector3 p, float radius) => false;
    public void ClickToMove(Vector3 p) => MoveTo(p, IsFlying() ? MoveType.Flying : MoveType.Ground);
    public void Stop() => StopMovement(); public void Jump() => Recoveries++; public void MoveBackward(int ms) => Recoveries++;
    public void Dismount() => SetMounted(false); public void InteractGameObject(ulong guid) => InteractObject(guid);
    public bool IsCasting { get { bool c = _casting; _casting = false; return c; } }
    public int GetHerbCount() { if (_inventoryPolls > 0 && --_inventoryPolls == 0) Herbs++; return Herbs; }
}
