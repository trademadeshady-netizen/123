using System.Text.Json;
using HerbBot.Routes;
using HerbBot.Data;
using HerbBot.Runtime;
using HerbBot.Maps;

// Консоль переносима между Windows/Linux; источник игры и карты заменяются в этой точке композиции.
string configFile = "herbbot.json";
var config = File.Exists(configFile)
    ? JsonSerializer.Deserialize<BotConfiguration>(File.ReadAllText(configFile), JsonFiles.Options) ?? throw new FormatException("Пустой конфиг")
    : new BotConfiguration();
if (!File.Exists(configFile)) JsonFiles.Write(configFile, config);
if (args.Contains("--inspect-gatherer"))
{
    var data = new GathererImporter().Load(config.GathererFile, Path.Combine(config.RoutesDirectory, "cache", "gatherer.json"), config.GameVersion);
    Console.WriteLine($"Нод: {data.Nodes.Count}; зон: {data.Nodes.Select(n => n.Zone).Distinct().Count()}; континенты: {string.Join(",", data.Nodes.Select(n => n.Continent).Distinct())}");
    foreach (var type in data.Nodes.GroupBy(n => n.Type)) Console.WriteLine($"{type.Key}: {type.Count()}");
    return;
}
var game = new UnavailableGameAdapter();
var routes = new RouteStore(config.RoutesDirectory);
var bot = new BotController(game, MapProviderFactory.Create(config, log: Console.WriteLine), routes, config, Console.WriteLine);
var hotkey = new ConsoleRecordHotkey();
IRecordHotkey recordKeys = OperatingSystem.IsWindows() ? new PollingRecordHotkey(new WindowsKeyStateProvider()) : hotkey;
var recorder = new RouteRecorder(game, routes, recordKeys);
var menu = new BotMenu(bot, routes, recorder, game, config);
Task? worker = null;
Console.WriteLine($"HerbBot {config.GameVersion}. R: {(OperatingSystem.IsWindows() ? "глобальный опрос Windows" : "консоль с фокусом")}");
Console.WriteLine("1 Старт | 2 Стоп | 3 Запись маршрута | R Добавить точку | 4 Остановить запись");
Console.WriteLine("5 Список/выбор маршрутов | 6 Зациклить | 7 Импорт трав Gatherer по зоне | 0 Выход");
if (Console.IsInputRedirected) { Console.WriteLine("Для меню нужен интерактивный терминал."); return; }
try
{
    while (true)
    {
        if (recorder.IsRecording)
        {
            try { recorder.Poll(); } catch (Exception ex) { Console.WriteLine("Ошибка записи: " + ex.Message); }
        }
        if (!Console.KeyAvailable) { await Task.Delay(50); continue; }
        var key = Console.ReadKey(true).Key;
        try
        {
            switch (key)
            {
                case ConsoleKey.D1:
                    if (bot.IsRunning) throw new InvalidOperationException("Бот уже запущен");
                    if (worker != null) await worker;
                    menu.Start();
                    worker = Task.Run(async () =>
                    {
                        try { while (bot.IsRunning) { bot.Tick(); await Task.Delay(100); } }
                        catch (Exception ex) { Console.WriteLine("Ошибка рабочего цикла: " + ex.Message); bot.RequestStop(); }
                    });
                    Console.WriteLine("Сбор запущен"); break;
                case ConsoleKey.D2:
                    menu.RequestStop(); if (worker != null) await worker; menu.Stop(); Console.WriteLine("Прогресс сохранён"); break;
                case ConsoleKey.D3:
                    Console.Write("Имя: "); menu.StartRecording(Console.ReadLine() ?? ""); break;
                case ConsoleKey.R:
                    hotkey.Feed(key); recorder.Poll(); Console.WriteLine($"Точек: {recorder.Count}; запись: {recorder.IsRecording}"); break;
                case ConsoleKey.D4:
                    var route = menu.StopRecording(); Console.WriteLine(route.Points.Count == 0 ? "Пустая запись завершена без файла" : $"Сохранён {route.Name}: {route.Points.Count} точек"); break;
                case ConsoleKey.D5:
                    Console.WriteLine(string.Join(Environment.NewLine, menu.ListRoutes()));
                    Console.Write("Выбрать имя: "); menu.Select(Console.ReadLine() ?? ""); break;
                case ConsoleKey.D6:
                    menu.ToggleLoop(); JsonFiles.Write(configFile, config); Console.WriteLine($"Зацикливание: {bot.Loop}"); break;
                case ConsoleKey.D7:
                    Console.Write("Код зоны, например AZSHARA: "); Console.WriteLine(bot.ImportGatherer(Console.ReadLine() ?? "")); break;
                case ConsoleKey.D0: return;
            }
        }
        catch (Exception ex) { Console.WriteLine("Ошибка: " + ex.Message); }
    }
}
finally
{
    bot.RequestStop(); if (worker != null) await worker; bot.Stop();
    if (recorder.IsRecording && recorder.Count > 0) recorder.Stop();
}
