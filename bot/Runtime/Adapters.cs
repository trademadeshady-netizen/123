using HerbBot.Data;
using HerbBot.Interfaces;
using HerbBot.Math;
using HerbBot.Routes;
namespace HerbBot.Runtime;

public sealed class BotConfiguration
{
    public string GameVersion { get; set; } = "3.3.5a";
    public HerbBot.Maps.MapOptions Maps { get; set; } = new();
    public double LootTimeoutSeconds { get; set; } = 5;
    public bool IgnoreIfPlayerNear { get; set; } = true;
    public string MapsDirectory { get; set; } = "mmaps";
    public string RoutesDirectory { get; set; } = "routes";
    public string GathererFile { get; set; } = "Gatherer.lua";
    // Значения из исходного ТЗ, точные значения WRobot требуют эксперимента.
    public string FarmingPolicySource { get; set; } = "ТЗ пользователя; WRobot НЕ ПОДТВЕРЖДЕНО";
    public int GatherAttempts { get; set; } = 3;
    public double BlacklistMinutes { get; set; } = 15;
    public double TravelTimeoutMarginSeconds { get; set; } = 5;
    public double StuckSeconds { get; set; } = 2;
    public float SearchRadius { get; set; } = 100f;
    public bool FlyingRoute { get; set; } = true;
    public bool LoopRoute { get; set; }
}

/// <summary>Провайдер владеет форматом mmaps и преобразованием координат; ядро не выдумывает рельеф.</summary>
public interface IMapProvider : ICollisionProvider
{
    void Load(int mapId, string gameVersion);
    List<Vector3> FindPath(Vector3 start, Vector3 end, bool flying);
    RoutePoint ToWorld(GatherNode node);
}
public sealed class MapDataUnavailableException(string message) : InvalidOperationException(message);
public sealed class UnavailableMapProvider(string directory) : IMapProvider
{
    private MapDataUnavailableException Error() => new($"Провайдер карт не подключён (путь: {Path.GetFullPath(directory)}). Подключите совместимый загрузчик mmaps и преобразование координат зон.");
    public void Load(int mapId, string gameVersion) => throw Error();
    public List<Vector3> FindPath(Vector3 start, Vector3 end, bool flying) => throw Error();
    public RoutePoint ToWorld(GatherNode node) => throw Error();
    public bool HasObstacle(Vector3 from, Vector3 to) => throw Error();
}
public sealed class MapPathFinder(IMapProvider maps) : IPathFinder
{
    public List<Vector3> FindPath(Vector3 start, Vector3 end) => maps.FindPath(start, end, false);
    public List<Vector3> FindFlyRoute(Vector3 start, Vector3 end) => maps.FindPath(start, end, true);
}
public enum MoveType { Ground, Flying }
public interface IGameAdapter : IPlayerPositionProvider
{
    // Методы по умолчанию сохраняют совместимость прежних адаптеров и явно запрещают неподключённый ввод.
    void MoveTo(Vector3 target, MoveType type) => throw new NotSupportedException("MoveTo: подключите механизм ввода.");
    void StopMovement() => throw new NotSupportedException("StopMovement: подключите механизм ввода.");
    void TargetObject(ulong guid) => throw new NotSupportedException("TargetObject: подключите механизм ввода.");
    void InteractObject(ulong guid) => throw new NotSupportedException("InteractObject: подключите механизм ввода.");
    bool IsLootWindowOpen() => throw new NotSupportedException("Чтение окна лута не подключено.");
    void LootAll() => throw new NotSupportedException("LootAll: подключите механизм ввода.");
    bool IsMounted() => throw new NotSupportedException("Состояние маунта не подключено.");
    void SetMounted(bool mounted) => throw new NotSupportedException("SetMounted: подключите механизм ввода.");
    bool IsFlying() => throw new NotSupportedException("Состояние полёта не подключено.");
    void Initialize(string gameVersion);
    /// <summary>Ограниченная по времени подготовка маунта/полёта. Ошибка, если режим недоступен.</summary>
    void PrepareTravel(bool flying);
    IObjectManager Objects { get; }
    IGameInput Input { get; }
    IGameActions Actions { get; }
}
/// <summary>Меню можно открыть без WoW. Запуск с отсутствующим адаптером выдаёт явную ошибку.</summary>
public sealed class UnavailableGameAdapter : IGameAdapter
{
    private static InvalidOperationException Error() => new("Игровой адаптер не подключён. Нужен источник мировых XYZ/mapId и игровых действий.");
    public void Initialize(string gameVersion) => throw Error();
    public void PrepareTravel(bool flying) => throw Error();
    public IObjectManager Objects => throw Error();
    public IGameInput Input => throw Error();
    public IGameActions Actions => throw Error();
    public RoutePoint GetPosition() => throw Error();
}
