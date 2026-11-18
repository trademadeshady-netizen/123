using HerbBot.Routes;
namespace HerbBot.Runtime;

/// <summary>Команды меню отделены от Console, чтобы проверять запуск и запись без клавиатуры.</summary>
public sealed class BotMenu(BotController bot, RouteStore routes, RouteRecorder recorder, IGameAdapter game, BotConfiguration config)
{
    public string? SelectedRoute { get; private set; }
    public IReadOnlyList<string> ListRoutes() => routes.List();
    public void Select(string name) { routes.Load(name); SelectedRoute = name; }
    public void Start() => bot.Start(SelectedRoute ?? throw new InvalidOperationException("Выберите маршрут"));
    public void RequestStop() => bot.RequestStop();
    public void Stop() => bot.Stop();
    public void StartRecording(string name) { game.Initialize(config.GameVersion); recorder.Start(name); }
    public HerbRoute StopRecording() => recorder.Stop();
    public void ToggleLoop() => bot.Loop = !bot.Loop;
}

/// <summary>Консоль сообщает нажатия R; глобальные хуки платформы можно подключить через IRecordHotkey.</summary>
public sealed class ConsoleRecordHotkey : IRecordHotkey
{
    private int _pending;
    public void Feed(ConsoleKey key) { if (key == ConsoleKey.R) Interlocked.Exchange(ref _pending, 1); }
    public bool TakeRecordPress() => Interlocked.Exchange(ref _pending, 0) == 1;
}
