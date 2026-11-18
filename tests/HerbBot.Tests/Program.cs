using HerbBot.Infrastructure;
using HerbBot.Interfaces;
using HerbBot.Math;
using HerbBot.Navigation;
using HerbBot.States;

using Xunit;

// Старые сценарии перенесены в стандартный запуск dotnet test.
[Collection("Sequential")]
public sealed class LegacyTests
{
public LegacyTests() { MathHelper.Collision = new EmptyWorldCollision(); }
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
[Fact]
public void MathTests()
{
    var a = new Vector3(0, 0, 0); var b = new Vector3(3, 4, 12);
    Assert(a.DistanceTo(b) == 13 && a.DistanceTo2D(b) == 5 && a.DistanceZ(b) == 12, "расстояния");
    Assert(MathF.Abs(a.InFrontOf(MathF.PI / 2, 2).Y - 2) < 0.001, "направление");
    Assert(MathF.Abs(MathHelper.AngleDifference(0.1f, 2 * MathF.PI - 0.1f) - 0.2f) < 0.001, "короткая дуга");
    var copy = a; copy.SetFlying(); Assert(a.IsGround() && copy.IsFlying(), "значение структуры");
}
[Fact]
public void PathTests()
{
    var finder = new PathFinder(); var start = new Vector3(0, 0, 0); var end = new Vector3(20, 0, 0);
    Assert(finder.FindPath(start, end).Count == 2, "прямой путь");
    MathHelper.Collision = new Obstacle((a, b) => CrossesWall(a, b));
    var path = finder.FindPath(start, end);
    Assert(path.Count > 2 && path.Zip(path.Skip(1)).All(pair => !MathHelper.TraceLine(pair.First, pair.Second)), "обход стены");
    MathHelper.Collision = new Obstacle((_, _) => true);
    try { finder.FindPath(start, end); throw new Exception("должен быть отказ"); }
    catch (InvalidOperationException) { }
    MathHelper.Collision = new Obstacle((_, _) => throw new InvalidOperationException("collision unavailable"));
    try { MathHelper.TraceLine(start, end); throw new Exception("ошибка столкновения скрыта"); }
    catch (InvalidOperationException) { }
}
static bool CrossesWall(Vector3 a, Vector3 b)
{
    if ((a.X < 5 && b.X >= 5) || (b.X < 5 && a.X >= 5))
    { float y = a.Y + (b.Y - a.Y) * (5 - a.X) / (b.X - a.X); return MathF.Abs(y) < 3; }
    return false;
}
[Fact]
public void FlightTests()
{
    var finder = new PathFinder();
    // Стена пересекает прямой путь и любой завершающий вертикальный спуск.
    MathHelper.Collision = new Obstacle((a, b) => (a.Z == 0 && b.Z == 0) || (a.X == 20 && b.X == 20));
    try { finder.FindFlyRoute(new(0, 0, 0), new(20, 0, 0)); throw new Exception("спуск сквозь стену"); }
    catch (InvalidOperationException) { }
}
[Fact]
public void MovementTests()
{
    var world = new World(); var input = new Input(); var movement = new MovementManager(input, world);
    Assert(!movement.IsStuck(), "в покое нет застревания");
    var path = new List<Vector3> { new(0, 0, 0, "Flying"), new(0, 0, 10, "Flying"), new(20, 0, 10, "Flying") };
    movement.Go(path, true);
    Assert(input.LastTarget.Z == 10 && input.LastTarget.X == 0, "пропущена вертикальная точка");
    movement.StopMove(); Assert(!movement.IsStuck(), "состояние застревания не сброшено");
}
static (BotContext, World, Input, Actions) Setup()
{
    var world = new World(); var input = new Input(); var actions = new Actions(world);
    var ctx = new BotContext { Objects = world, PathFinder = new PathFinder(), Movement = new MovementManager(input, world), Actions = actions };
    return (ctx, world, input, actions);
}
[Fact]
public void SelectionAndGather()
{
    var (ctx, world, _, actions) = Setup();
    world.Nodes.Add(new() { Guid = 1, Position = new(2, 0, 0) });
    world.Nodes.Add(new() { Guid = 2, Position = new(3, 0, 0) });
    world.Nodes.Add(new() { Guid = 3, Position = new(200, 0, 0) });
    ctx.Blacklist.Add(1, TimeSpan.FromMinutes(1)); actions.SuccessOn = 1; actions.RemoveOnSuccess = true;
    new FarmingState(ctx).Run();
    Assert(actions.LastGuid == 2 && actions.Attempts == 1, "неправильный выбор/сбор");
    Assert(ctx.Blacklist.IsBlacklisted(2), "собранная трава не в списке");
}
[Fact]
public void Retry()
{
    var (ctx, world, _, actions) = Setup(); var node = new WoWGameObject { Guid = 8, Position = new(1, 0, 0) };
    world.Nodes.Add(node); actions.SuccessOn = 3;
    Assert(new FarmingTask(ctx).Pulse(node) && actions.Attempts == 3, "ошибка повторов");
    Assert(ctx.Blacklist.IsBlacklisted(8), "отсутствует список");
}
[Fact]
public void Disappearing()
{
    var (ctx, world, input, actions) = Setup(); var node = new WoWGameObject { Guid = 9, Position = new(20, 0, 0) };
    world.Nodes.Add(node); input.OnMove = () => world.Nodes.Clear();
    Assert(!new FarmingTask(ctx).Pulse(node) && actions.Attempts == 0 && input.Stops > 0, "объект не отменён");
}
[Fact]
public void ConditionsTest()
{
    var (ctx, world, _, _) = Setup(); var state = new FarmingState(ctx);
    Assert(state.NeedToRun(), "обычное состояние");
    world.Player.InCombat = true; Assert(!state.NeedToRun(), "бой"); world.Player.InCombat = false;
    world.Player.Health = 0; Assert(!state.NeedToRun(), "смерть"); world.Player.Health = 1;
    ctx.IsPaused = true; Assert(!state.NeedToRun(), "пауза"); ctx.IsPaused = false;
    ctx.LongMove.IsActive = true; Assert(!state.NeedToRun(), "длительное движение");
    ctx.Settings.HarvestDuringLongMove = true; Assert(state.NeedToRun(), "разрешённый сбор");
}
[Fact]
public void StuckTest()
{
    var world = new World(); var input = new Input(); var movement = new MovementManager(input, world);
    movement.Go(new() { new(0, 0, 0), new(10, 0, 0) }, true);
    Thread.Sleep(2100);
    Assert(movement.IsStuck() && input.Recoveries == 1, "не выполнено восстановление");
    movement.StopMove();
    Assert(!movement.IsStuck(), "сброс после остановки");
}
[Fact]
public void ReadFailure()
{
    var (ctx, world, input, actions) = Setup(); var node = new WoWGameObject { Guid = 10, Position = new(20, 0, 0) };
    world.Nodes.Add(node); input.OnMove = () => world.ThrowOnValid = true;
    Assert(!new FarmingTask(ctx).Pulse(node) && input.Stops > 0 && actions.Attempts == 0, "ошибка не обработана");
}
[Fact]
public void TimeoutTest()
{
    var (ctx, world, input, _) = Setup(); var node = new WoWGameObject { Guid = 11, Position = new(6, 0, 0) };
    world.Player.Speed = 10000; world.Nodes.Add(node);
    Assert(!new FarmingTask(ctx).Pulse(node) && input.Stops > 0 && ctx.Blacklist.IsBlacklisted(11), "таймаут не обработан");
}
[Fact]
public void RetryFailure()
{
    var (ctx, world, _, actions) = Setup(); var node = new WoWGameObject { Guid = 12, Position = new(1, 0, 0) };
    world.Nodes.Add(node);
    Assert(!new FarmingTask(ctx).Pulse(node) && actions.Attempts == 3 && ctx.Blacklist.IsBlacklisted(12), "неудачный сбор");
}
[Fact]
public void SelectionFilters()
{
    var (ctx, world, _, actions) = Setup();
    // Случайный множитель не перекрывает здесь штраф полного разворота.
    world.Nodes.Add(new() { Guid = 21, Position = new(-3.5f, 0, 0) });
    world.Nodes.Add(new() { Guid = 22, Position = new(3.6f, 0, 0) });
    world.Nodes.Add(new() { Guid = 23, Position = new(0.1f, 0, 0) });
    world.NearPlayer = p => p.X == 0.1f;
    actions.SuccessOn = 1;
    new FarmingState(ctx).Run();
    Assert(actions.LastGuid == 22, "выбор с учётом угла или игрока рядом");
}
}
sealed class Obstacle(Func<Vector3, Vector3, bool> f) : ICollisionProvider { public bool HasObstacle(Vector3 a, Vector3 b) => f(a, b); }
sealed class World : IObjectManager
{
    public LocalPlayer Player = new(); public List<WoWGameObject> Nodes = new(); public bool ThrowOnValid; public Func<Vector3, bool>? NearPlayer;
    public LocalPlayer GetLocalPlayer() => Player;
    public IReadOnlyList<WoWGameObject> GetWoWGameObjectForFarm() => Nodes.ToArray();
    public WoWGameObject? GetWoWGameObjectByName(string name) => Nodes.FirstOrDefault(n => n.Name == name);
    public WoWGameObject? GetWoWGameObjectByEntry(int id) => Nodes.FirstOrDefault(n => n.Entry == id);
    public bool IsValid(ulong guid) => ThrowOnValid ? throw new InvalidOperationException("object unavailable") : Nodes.Any(n => n.Guid == guid);
    public bool IsOtherPlayerNear(Vector3 point, float radius) => NearPlayer?.Invoke(point) ?? false;
}
sealed class Input : IGameInput
{
    public Vector3 LastTarget; public int Stops, Recoveries; public Action? OnMove;
    public void ClickToMove(Vector3 target) { LastTarget = target; OnMove?.Invoke(); }
    public void Stop() => Stops++;
    public void Jump() => Recoveries++;
    public void MoveBackward(int ms) => Recoveries++;
}
sealed class Actions(World world) : IGameActions
{
    public int Attempts, SuccessOn = int.MaxValue, Count; public ulong LastGuid; public bool RemoveOnSuccess;
    public bool IsCasting => false;
    public void Dismount() { }
    public int GetHerbCount() => Count;
    public void InteractGameObject(ulong guid)
    {
        LastGuid = guid;
        if (++Attempts == SuccessOn) { Count++; if (RemoveOnSuccess) world.Nodes.Clear(); }
    }
}
