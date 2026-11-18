using System.Buffers.Binary;
using HerbBot.Data;
using HerbBot.Math;
using HerbBot.Routes;
using HerbBot.Runtime;
namespace HerbBot.Maps;

public sealed class MapDataMissingException(string message) : IOException(message);
public sealed class MapOptions
{
    public string Provider { get; set; } = "mmap";
    public string MissingData { get; set; } = "fail";
    // Версии формата из ветки TrinityCore 3.3.5; это не номер сборки клиента.
    public uint MmapVersion { get; set; } = 15;
    public uint DetourVersion { get; set; } = 7;
}
public sealed record MmapParameters(float[] Origin, float TileWidth, float TileHeight, int MaxTiles, int MaxPolygons);
public sealed record MmapTile(string Path, int X, int Y, int Layer, int Polygons, int Vertices, float[] Min, float[] Max, bool UsesLiquids);
public sealed record MmapData(MmapParameters Parameters, IReadOnlyList<MmapTile> Tiles);

/// <summary>Загрузчик проверяет файл, а вычисления Detour/коллизии/проекция подключаются отдельно.
/// Навмеш земли сам по себе не доказывает отсутствие препятствий в воздухе.</summary>
public interface IMapNavigationBackend : IMapProvider
{
    void LoadValidated(MmapData data);
}
public sealed class MmapMapProvider(string directory, MapOptions options, IMapNavigationBackend? backend = null, Action<string>? log = null) : IMapProvider
{
    private bool _loaded;
    private Exception? _loadError;
    public MmapData? Data { get; private set; }
    public void Load(int mapId, string gameVersion)
    {
        _loaded = false; Data = null; _loadError = null;
        if (mapId is < 0 or > 999 || gameVersion != "3.3.5a") throw new NotSupportedException("Загрузчик рассчитан на имена карт TrinityCore 3.3.5 (000..999).");
        if (options.MissingData != "fail" && options.MissingData != "warn_and_continue") throw new ArgumentException("MissingData: fail или warn_and_continue.");
        string map = Path.Combine(directory, $"{mapId:D3}.mmap");
        try
        {
            if (!File.Exists(map)) throw Missing(map, gameVersion);
            var files = Directory.GetFiles(directory, $"{mapId:D3}????.mmtile").Order(StringComparer.Ordinal).ToArray();
            if (files.Length == 0) throw Missing(Path.Combine(directory, $"{mapId:D3}XXYY.mmtile"), gameVersion);
            var parameters = ReadParameters(map);
            var tiles = files.Select(f => ReadTile(f, options)).ToArray();
            if (tiles.Length > parameters.MaxTiles || tiles.Any(t => t.Polygons > parameters.MaxPolygons) || tiles.Select(t => (t.X, t.Y, t.Layer)).Distinct().Count() != tiles.Length)
                throw new InvalidDataException("Число/ключи тайлов не согласованы с параметрами карты.");
            Data = new(parameters, tiles);
            // Успешный разбор заголовков не означает готовность поиска пути.
            if (backend != null) { backend.LoadValidated(Data); backend.Load(mapId, gameVersion); }
            _loaded = true;
        }
        catch (MapDataMissingException ex) when (options.MissingData == "warn_and_continue")
        {
            _loadError = ex; log?.Invoke("ПРЕДУПРЕЖДЕНИЕ: " + ex.Message + " Интерфейс доступен, навигация запрещена.");
        }
    }
    private MapDataMissingException Missing(string path, string version) => new($"Нет данных карты: {Path.GetFullPath(path)}. Сгенерируйте mmaps из легальной копии данных клиента {version} совместимым TrinityCore mmaps_generator (MMAP {options.MmapVersion}, Detour {options.DetourVersion}); скопируйте .mmap и .mmtile в {Path.GetFullPath(directory)}.");
    private IMapNavigationBackend Navigation()
    {
        if (_loadError != null) throw new MapDataMissingException(_loadError.Message);
        if (!_loaded) throw new InvalidOperationException("Сначала загрузите карту.");
        return backend ?? throw new NotSupportedException("Заголовки mmaps проверены; backend Detour, коллизий полёта и преобразования координат ещё не подключён. Прямой путь не подставляется.");
    }
    public List<Vector3> FindPath(Vector3 start, Vector3 end, bool flying) => Navigation().FindPath(start, end, flying);
    public bool HasObstacle(Vector3 from, Vector3 to) => Navigation().HasObstacle(from, to);
    public RoutePoint ToWorld(GatherNode node) => Navigation().ToWorld(node);

    public static MmapParameters ReadParameters(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length != 28) throw new InvalidDataException(".mmap должен содержать 28 байт dtNavMeshParams, без заголовка MMAP.");
        byte[] b = new byte[28]; stream.ReadExactly(b);
        float F(int i) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i)));
        int I(int i) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i));
        if (Enumerable.Range(0, 5).Any(i => !float.IsFinite(F(i * 4))) || F(12) <= 0 || F(16) <= 0 || I(20) <= 0 || I(24) <= 0)
            throw new InvalidDataException("Неверные параметры навмеша.");
        return new([F(0), F(4), F(8)], F(12), F(16), I(20), I(24));
    }
    public static MmapTile ReadTile(string path, MapOptions options)
    {
        // Читаем только ограниченный заголовок, не выделяем память по недоверенному size.
        using var stream = File.OpenRead(path); byte[] b = new byte[120]; stream.ReadExactly(b);
        uint U(int i) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i));
        int I(int i) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i));
        float F(int i) => BitConverter.Int32BitsToSingle(I(i));
        if (U(0) != 0x4d4d4150 || U(4) != options.DetourVersion || U(8) != options.MmapVersion || U(12) != stream.Length - 20 || b[16] > 1)
            throw new InvalidDataException("Неверный MmapTileHeader: magic, версии, size или usesLiquids.");
        if (U(20) != 0x444e4156 || U(24) != options.DetourVersion) throw new InvalidDataException("Неверный dtMeshHeader.");
        if (Enumerable.Range(0, 9).Any(n => I(44 + n * 4) < 0) || I(44) == 0 || I(48) == 0 || I(76) > I(44))
            throw new InvalidDataException("Неверное число полигонов/вершин/деталей.");
        if (Enumerable.Range(0, 10).Any(n => !float.IsFinite(F(80 + n * 4)))) throw new InvalidDataException("Неверные bounds/параметры тайла.");
        float[] min = [F(92), F(96), F(100)], max = [F(104), F(108), F(112)];
        if (Enumerable.Range(0, 3).Any(n => min[n] > max[n]) || F(80) < 0 || F(84) < 0 || F(88) < 0 || F(116) < 0)
            throw new InvalidDataException("Неверные bounds тайла.");
        // Минимальный размер известных секций: вершины float[3], dtPoly (32 байта).
        // Остальные секции, ссылки и индексы обязан проверить подключаемый Detour backend.
        if (100L + I(48) * 12L + I(44) * 32L > U(12)) throw new InvalidDataException("Обрезаны секции вершин/полигонов.");
        return new(path, I(28), I(32), I(36), I(44), I(48), min, max, b[16] != 0);
    }
}
/// <summary>ТОЛЬКО ТЕСТЫ. Прямой отрезок не проверяет препятствия и не пригоден для игры.</summary>
public sealed class StraightLineMapProvider : IMapProvider
{
    public StraightLineMapProvider(bool isTestEnvironment)
    { if (!isTestEnvironment) throw new InvalidOperationException("StraightLineMapProvider запрещён в проде: только тесты, без коллизий."); }
    public void Load(int mapId, string gameVersion) { }
    public List<Vector3> FindPath(Vector3 start, Vector3 end, bool flying) => [start, end];
    public bool HasObstacle(Vector3 from, Vector3 to) => false;
    public RoutePoint ToWorld(GatherNode node) => throw new NotSupportedException("Тестовый провайдер не преобразует координаты Gatherer.");
}
public static class MapProviderFactory
{
    public static IMapProvider Create(BotConfiguration config, bool isTestEnvironment = false, IMapNavigationBackend? backend = null, Action<string>? log = null) => config.Maps.Provider switch
    {
        "mmap" => new MmapMapProvider(config.MapsDirectory, config.Maps, backend, log),
        "straight_line_test" => new StraightLineMapProvider(isTestEnvironment),
        _ => throw new ArgumentException("Неизвестный провайдер карт: " + config.Maps.Provider)
    };
}
