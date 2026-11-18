using System.Text.Json;
using HerbBot.Math;
namespace HerbBot.Routes;

public sealed record RoutePoint(float X, float Y, float Z, int MapId, DateTimeOffset Timestamp)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public Vector3 Position => new(X, Y, Z);
}
public sealed record HerbRoute(int SchemaVersion, string Name, List<RoutePoint> Points);
public sealed record RouteProgress(string Route, int NextPoint, bool Completed, DateTimeOffset SavedAt);

public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Options)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

/// <summary>Отдельные JSON-файлы не изменяют базу Gatherer и легко проверяются человеком.</summary>
public sealed class RouteStore(string directory)
{
    public string DirectoryPath => Path.GetFullPath(directory);
    public IReadOnlyList<string> List() => Directory.Exists(directory)
        ? Directory.GetFiles(directory, "*.json").Select(Path.GetFileNameWithoutExtension).Cast<string>().Order().ToArray() : [];
    public void Save(HerbRoute route) { Validate(route); JsonFiles.Write(PathFor(route.Name), route); }
    public HerbRoute Load(string name)
    {
        var route = JsonSerializer.Deserialize<HerbRoute>(File.ReadAllText(PathFor(name)), JsonFiles.Options) ?? throw new FormatException("Пустой маршрут");
        Validate(route); if (route.Name != name) throw new FormatException("Имя маршрута не совпадает с файлом"); return route;
    }
    public void SaveProgress(RouteProgress progress) => JsonFiles.Write(Path.Combine(directory, "progress", ValidateName(progress.Route) + ".json"), progress);
    private string PathFor(string name) => Path.Combine(directory, ValidateName(name) + ".json");
    public static string ValidateName(string name) => name.Length is > 0 and <= 80 && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
        ? name : throw new ArgumentException("Имя маршрута: буквы, цифры, '-' и '_', до 80 символов.");
    public static void Validate(HerbRoute route)
    {
        if (route.SchemaVersion != 1 || route.Points is not { Count: > 0 }) throw new FormatException("Неподдерживаемый или пустой маршрут");
        foreach (var p in route.Points)
            if (p is null || p.MapId < 0 || !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) throw new FormatException("Неверные координаты маршрута");
    }
    public static int Nearest(HerbRoute route, RoutePoint player)
    {
        Validate(route);
        // В полёте одинаковые XY могут находиться на разных высотах: используем 3D.
        var indices = Enumerable.Range(0, route.Points.Count).Where(i => route.Points[i].MapId == player.MapId).ToArray();
        if (indices.Length == 0) throw new InvalidOperationException("В маршруте нет точек текущей карты.");
        return indices.MinBy(i => route.Points[i].Position.DistanceTo(player.Position));
    }
}
