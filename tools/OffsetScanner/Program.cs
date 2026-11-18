using System.Globalization;
using System.Text.Json;
using HerbBot.Memory;
using HerbBot.Objects;
using HerbBot.Data;
using HerbBot.Routes;

// Утилита проверяет заданные кандидаты, не объявляет автоматически найденные числа смещениями клиента.
if (args.Length is < 4 or > 5)
{
    Console.WriteLine("OffsetScanner <PID> <profile.json> <GUID hex из /dump UnitGUID(\"player\")> <ожидаемый mapId> [Gatherer.lua для object manager]");
    return 2;
}
try
{
    var profile = JsonSerializer.Deserialize<ClientProfile>(File.ReadAllText(args[1]), JsonFiles.Options) ?? throw new FormatException("Пустой профиль");
    string hex = args[2].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[2][2..] : args[2];
    ulong expected = ulong.Parse(hex, NumberStyles.HexNumber); int map = int.Parse(args[3]);
    using var memory = WindowsProcessMemory.Attach(int.Parse(args[0]), profile);
    var provider = new MemoryPositionProvider(memory, profile);
    Console.WriteLine($"База модуля: 0x{memory.ModuleBase:X}. В течение 10 секунд двигайтесь и меняйте высоту.");
    MemoryObjectScanner? scanner = null;
    if (args.Length == 5)
    {
        var entries = new GathererImporter().Parse(File.ReadAllText(args[4])).Where(n => n.Type == "HERB" && n.Entry.HasValue).Select(n => n.Entry!.Value).ToHashSet();
        if (entries.Count == 0) throw new FormatException("В Gatherer нет числовых HERB entry.");
        scanner = new(memory, provider, profile.ObjectManager ?? throw new FormatException("Нет layout ObjectManager"), entries);
        Console.WriteLine("Подойдите к известной траве, запишите её серверный entry и XYZ, затем соберите. Имён и мирового Z в Gatherer может не быть: их совпадение не утверждается.");
    }
    var objects = new List<object>(); var objectSets = new List<string>(); float? minimumDistance = null;
    var samples = new List<PlayerSnapshot>();
    for (int i = 0; i < 20; i++)
    {
        var value = provider.GetSnapshot(); samples.Add(value);
        Console.WriteLine(JsonSerializer.Serialize(value, JsonFiles.Options));
        if (scanner != null)
        {
            var herbs = scanner.GetVisibleHerbs(200);
            objectSets.Add(string.Join(",", herbs.Select(n => n.Guid).Order()));
            foreach (var herb in herbs)
            {
                float distance = value.Position.Position.DistanceTo(herb.Position);
                minimumDistance = minimumDistance.HasValue ? MathF.Min(minimumDistance.Value, distance) : distance;
            }
            objects.Add(new { timestamp = DateTimeOffset.UtcNow, herbs });
            Console.WriteLine($"HERB entry из Gatherer: {herbs.Count}; minDistance={minimumDistance}");
        }
        await Task.Delay(500);
    }
    bool guid = samples.All(s => s.Guid == expected), mapOk = samples.All(s => s.Position.MapId == map);
    bool x = samples.Select(s => s.Position.X).Distinct().Count() > 1;
    bool y = samples.Select(s => s.Position.Y).Distinct().Count() > 1;
    bool z = samples.Select(s => s.Position.Z).Distinct().Count() > 1;
    JsonFiles.Write("offset-report.json", new { generatedBy = "CodeRabbit HerbBot OffsetScanner", profile.Build, memory.ModuleBase, guid, mapOk, x, y, z, samples,
        objectManager = new { enabled = scanner != null, candidateListChanged = objectSets.Distinct().Count() > 1, minimumDistance, objects,
            status = "ТРЕБУЕТ ЭКСПЕРИМЕНТА: фильтр по известным entry не доказывает полноту обхода и правильность layout; сопоставьте с сервером/аддоном и контрольной MINE." } });
    Console.WriteLine($"GUID={guid}, MapId={mapOk}, изменения X/Y/Z={x}/{y}/{z}. Флаги и Rotation требуют сопоставления с действиями игрока.");
    return guid && mapOk && x && y && z && (scanner == null || (objectSets.Distinct().Count() > 1 && minimumDistance <= 4.5f)) ? 0 : 1;
}
catch (Exception ex) { Console.Error.WriteLine("НЕ ПОДТВЕРЖДЕНО: " + ex.Message); return 1; }
