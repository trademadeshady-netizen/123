using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace HerbBot.Data;

/// <summary>XY остаются координатами зоны; неизвестные mapId/Z не заменяем нулём.</summary>
public sealed record GatherNode(int Continent, string Zone, int? Entry, string EntryKey, string Type, double X, double Y,
    int? MapId, double? Z, string? Name, int? Priority, string? Source, Dictionary<string, object?> RawFields);
public sealed record GatherCache(int SchemaVersion, string SourceSha256, string GameVersion, List<GatherNode> Nodes);

public sealed class GathererImporter
{
    public List<GatherNode> Parse(string text)
    {
        var globals = new LuaTableParser().Parse(text);
        var root = Table(globals.GetValueOrDefault("GatherItems"));
        var result = new List<GatherNode>();
        foreach (var (continentKey, continentValue) in root)
        {
            if (!int.TryParse(continentKey, out int continent)) continue;
            foreach (var (zone, zoneValue) in Table(continentValue))
            foreach (var (entryKey, entryValue) in Table(zoneValue))
            {
                int? entry = int.TryParse(entryKey, out int id) ? id : null;
                var group = Table(entryValue);
                string type = group.GetValueOrDefault("gtype") as string ?? throw new FormatException("Отсутствует gtype");
                foreach (var (rowKey, rowValue) in group)
                {
                    if (!int.TryParse(rowKey, out _)) continue;
                    var row = Table(rowValue);
                    double x = Number(row, "1"), y = Number(row, "2");
                    if (x is < 0 or > 1 || y is < 0 or > 1) throw new FormatException("Ожидались XY зоны в диапазоне 0..1");
                    result.Add(new(continent, zone, entry, entryKey, type, x, y, null, null, null, null,
                        row.GetValueOrDefault("6") as string, row));
                }
            }
        }
        if (result.Count == 0) throw new FormatException("GatherItems не содержит нод.");
        return result;
    }
    public GatherCache Load(string luaPath, string cachePath, string gameVersion)
    {
        // Хеш исходника и версия схемы исключают использование устаревшего кэша.
        string text = File.ReadAllText(luaPath);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        if (File.Exists(cachePath))
        {
            try
            {
                var cache = JsonSerializer.Deserialize<GatherCache>(File.ReadAllText(cachePath), Routes.JsonFiles.Options);
                if (cache is { SchemaVersion: 2, Nodes.Count: > 0 } && cache.SourceSha256 == hash && cache.GameVersion == gameVersion) return cache;
            }
            catch (JsonException) { /* Повреждённый кэш восстанавливаем из исходника. */ }
        }
        var fresh = new GatherCache(2, hash, gameVersion, Parse(text));
        Routes.JsonFiles.Write(cachePath, fresh);
        return fresh;
    }
    private static Dictionary<string, object?> Table(object? value) => value as Dictionary<string, object?> ?? throw new FormatException("Ожидалась таблица");
    private static double Number(Dictionary<string, object?> row, string key) => row.GetValueOrDefault(key) is double n ? n : throw new FormatException("Ожидалось число " + key);
}
