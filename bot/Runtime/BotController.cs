using HerbBot.Data;
using HerbBot.Infrastructure;
using HerbBot.Math;
using HerbBot.Navigation;
using HerbBot.Routes;
using HerbBot.States;
namespace HerbBot.Runtime;

/// <summary>Один рабочий поток вызывает Tick. Стоп из меню сначала выставляет запрос прерывания.</summary>
public sealed class BotController(IGameAdapter game, IMapProvider maps, RouteStore store, BotConfiguration config, Action<string> log)
{
    private volatile string _state = "idle";
    public string State { get => _state; private set => _state = value; }
    private BotContext? _context;
    private FarmingState? _farming;
    private HerbRoute? _route;
    private List<Vector3>? _path;
    private long _pathStarted;
    private double _pathTimeout;
    private volatile bool _stopRequested;
    private bool _completed;
    private volatile bool _running;
    public bool IsRunning { get => _running; private set => _running = value; }
    public int NextPoint { get; private set; }
    public bool Loop { get => config.LoopRoute; set => config.LoopRoute = value; }

    public void Start(string routeName)
    {
        if (IsRunning) throw new InvalidOperationException("Бот уже запущен");
        if (!double.IsFinite(config.LootTimeoutSeconds) || config.LootTimeoutSeconds <= 0 || config.GatherAttempts is < 1 or > 100 || !double.IsFinite(config.BlacklistMinutes) || config.BlacklistMinutes <= 0 || !double.IsFinite(config.StuckSeconds) || config.StuckSeconds <= 0 || !double.IsFinite(config.TravelTimeoutMarginSeconds) || config.TravelTimeoutMarginSeconds < 0 || !float.IsFinite(config.SearchRadius) || config.SearchRadius <= 0)
            throw new InvalidOperationException("Неверные параметры сбора/таймаутов.");
        _stopRequested = false; _completed = false;
        _route = null; _context = null; _farming = null; _path = null;
        try
        {
            game.Initialize(config.GameVersion);
            _route = store.Load(routeName);
            var player = game.GetPosition();
            maps.Load(player.MapId, config.GameVersion);
            NextPoint = RouteStore.Nearest(_route, player);
            _context = new BotContext { Log = log, Game = game is IExplicitLootAdapter ? game : null, Objects = game.Objects, Actions = game.Actions,
                Movement = new MovementManager(game.Input, game.Objects, config.StuckSeconds), PathFinder = new MapPathFinder(maps) };
            _context.Settings.IgnoreIfPlayerNear = config.IgnoreIfPlayerNear;
            _context.Settings.LootTimeoutSeconds = config.LootTimeoutSeconds;
            _context.Settings.GatherAttempts = config.GatherAttempts;
            _context.Settings.TravelTimeoutMarginSeconds = config.TravelTimeoutMarginSeconds;
            _context.Settings.BlacklistDuration = TimeSpan.FromMinutes(config.BlacklistMinutes);
            _context.Settings.SearchRadiusObjects = config.SearchRadius;
            _context.Settings.HarvestDuringLongMove = true;
            _context.LongMove.IsActive = true;
            MathHelper.Collision = maps;
            _farming = new FarmingState(_context, () => State = "gathering");
            BuildPath(player);
            State = "flying to route"; IsRunning = true;
            Save(false);
        }
        catch (Exception ex)
        {
            RequestStop(); _context?.Movement.StopMove(); IsRunning = false; State = "stopped";
            log("Ошибка старта: " + ex.Message); throw;
        }
    }

    public string ImportGatherer(string zone)
    {
        if (IsRunning) throw new InvalidOperationException("Остановите бота перед импортом");
        game.Initialize(config.GameVersion);
        var player = game.GetPosition(); maps.Load(player.MapId, config.GameVersion);
        var cache = new GathererImporter().Load(config.GathererFile, Path.Combine(store.DirectoryPath, "cache", "gatherer.json"), config.GameVersion);
        // Сохраняем порядок исходника, не выдавая его за алгоритм обхода WRobot.
        var points = cache.Nodes.Where(n => n.Type == "HERB" && n.Zone == zone).Select(maps.ToWorld).ToList();
        var route = new HerbRoute(1, "Gatherer_" + zone, points); store.Save(route); return route.Name;
    }

    public void Tick()
    {
        if (!IsRunning) return;
        try
        {
            if (_stopRequested) { Stop(); return; }
            var current = game.GetPosition();
            var target = _route!.Points[NextPoint];
            if (current.MapId != target.MapId) throw new InvalidOperationException("Переход между картами требует отдельного маршрута.");
            var me = game.Objects.GetLocalPlayer();
            if (me.IsDead || me.InCombat)
            { _context!.Movement.StopMove(); _path = null; return; }
            // Останавливаем маршрут перед уходом к траве; затем строим путь к той же путевой точке.
            if (_farming!.TryRun()) { State = "returning"; _path = null; return; }
            if (current.Position.DistanceTo(target.Position) <= 1.5f)
            {
                _context!.Movement.StopMove(); _path = null;
                if (NextPoint + 1 < _route.Points.Count) NextPoint++;
                else if (Loop) NextPoint = 0;
                else { _completed = true; Save(true); IsRunning = false; State = "stopped"; return; }
                Save(false); return;
            }
            if (_path == null) BuildPath(current);
            if (System.Diagnostics.Stopwatch.GetElapsedTime(_pathStarted).TotalSeconds > _pathTimeout)
                throw new InvalidOperationException("Таймаут движения к точке маршрута");
            _context!.Movement.Go(_path!, true); _context.Movement.IsStuck();
        }
        catch (Exception ex) { log("Остановка: " + ex.Message); Stop(); }
    }
    private void BuildPath(RoutePoint current)
    {
        var target = _route!.Points[NextPoint];
        if (current.MapId != target.MapId) throw new InvalidOperationException("Нельзя строить путь между картами");
        _path = maps.FindPath(current.Position, target.Position, config.FlyingRoute);
        if (_path.Count < 2 || _path[0].DistanceTo(current.Position) > 1.5f || _path[^1].DistanceTo(target.Position) > 1.5f)
            throw new InvalidOperationException("Провайдер карт вернул неполный путь");
        for (int i = 1; i < _path.Count; i++)
            if (maps.HasObstacle(_path[i - 1], _path[i])) throw new InvalidOperationException("Маршрут пересекает препятствие");
        // После спешивания у травы возвращаем требуемый режим маршрута через адаптер.
        game.PrepareTravel(config.FlyingRoute);
        _pathStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _pathTimeout = current.Position.DistanceTo(target.Position) / MathF.Max(0.1f, game.Objects.GetLocalPlayer().Speed) + config.TravelTimeoutMarginSeconds;
    }
    public void RequestStop()
    {
        _stopRequested = true;
        if (_context != null) _context.IsPaused = true; // FarmingTask проверяет флаг в цикле движения/каста.
    }
    public void Stop()
    {
        RequestStop();
        try { _context?.Movement.StopMove(); }
        finally { _path = null; IsRunning = false; State = "stopped"; Save(_completed); }
    }
    private void Save(bool completed)
    {
        if (_route != null) store.SaveProgress(new(_route.Name, NextPoint, completed, DateTimeOffset.UtcNow));
    }
}
