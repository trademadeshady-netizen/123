using System.Diagnostics;
using System.Text.Json;
using HerbBot.Memory;
using HerbBot.Objects;
using HerbBot.Maps;
using HerbBot.Data;
using HerbBot.Routes;
using HerbBot.Runtime;
namespace HerbBot.WinForms;

public sealed class MainForm : Form
{
    private const string ConfigFile = "herbbot.json";
    private readonly ComboBox _processes = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly ComboBox _routes = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly TextBox _routeName = new() { Width = 140, PlaceholderText = "Имя маршрута" };
    private readonly TextBox _zone = new() { Width = 140, PlaceholderText = "Код зоны" };
    private readonly CheckBox _loop = new() { Text = "Зациклить маршрут", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Text = "Клиент не подключён" };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Button _start = new() { Text = "Старт", AutoSize = true };
    private readonly Button _stop = new() { Text = "Стоп", AutoSize = true };
    private readonly Button _attach = new() { Text = "Подключить профиль памяти", AutoSize = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
    private IGameAdapter _game = new UnavailableGameAdapter();
    private readonly BotConfiguration _config;
    private readonly RouteStore _store;
    private BotController _controller;
    private readonly RegisteredRecordHotkey _hotkey = new();
    private readonly string _logPath = Path.Combine("logs", $"herbbot-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    private WindowsProcessMemory? _memory;
    private MemoryPositionProvider? _position;
    private RouteRecorder? _recorder;
    private Task? _worker;
    private Task? _launch;
    private bool _cancelStart;
    private bool _closing;
    private bool _stopping;

    public MainForm()
    {
        Text = "HerbBot — WotLK 3.3.5a"; Width = 920; Height = 580; MinimumSize = new(720, 460);
        _config = File.Exists(ConfigFile)
            ? JsonSerializer.Deserialize<BotConfiguration>(File.ReadAllText(ConfigFile), JsonFiles.Options) ?? throw new FormatException("Пустой конфиг")
            : new BotConfiguration();
        if (!File.Exists(ConfigFile)) JsonFiles.Write(ConfigFile, _config);
        _store = new RouteStore(_config.RoutesDirectory);
        _controller = new BotController(_game, MapProviderFactory.Create(_config, log: Log), _store, _config, Log);
        _loop.Checked = _controller.Loop;
        _loop.CheckedChanged += (_, _) => Run(() => { _controller.Loop = _loop.Checked; JsonFiles.Write(ConfigFile, _config); });

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(8) };
        controls.Controls.AddRange([new Label { Text = "Процесс:", AutoSize = true }, _processes,
            MakeButton("Обновить процессы", RefreshProcesses), _attach,
            new Label { Text = "Маршрут:", AutoSize = true }, _routes, MakeButton("Обновить маршруты", RefreshRoutes),
            _routeName, MakeButton("Запись маршрута", StartRecording), MakeButton("Остановить запись", StopRecording),
            _start, _stop, _loop, _zone, MakeButton("Импорт трав Gatherer", ImportGatherer), _status]);
        layout.Controls.Add(controls, 0, 0); layout.Controls.Add(_log, 0, 1); Controls.Add(layout);
        _attach.Click += (_, _) => Run(Attach);
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += async (_, _) => await StopAsync();
        _timer.Tick += (_, _) => PollRecording(); _timer.Start();
        FormClosing += OnClosing;
        RefreshProcesses(); RefreshRoutes();
        Log("Запись: подключите Wow.exe и профиль памяти, затем нажимайте R. Запуск сбора требует адаптер игры и карт.");
    }
    private static Button MakeButton(string title, Action action)
    {
        var button = new Button { Text = title, AutoSize = true };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "HerbBot", MessageBoxButtons.OK, MessageBoxIcon.Warning); } };
        return button;
    }
    private void Run(Action action) { try { action(); } catch (Exception ex) { Log("Ошибка: " + ex.Message); } }
    private void Log(string message)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { try { BeginInvoke(() => Log(message)); } catch (InvalidOperationException) { } return; }
        try { Directory.CreateDirectory("logs"); File.AppendAllText(_logPath, $"{DateTime.Now:O} {message}{Environment.NewLine}"); }
        catch (IOException ex) { _status.Text = "Ошибка файла журнала: " + ex.Message; }
        _log.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
    }
    private sealed record ProcessChoice(int Id, string Name) { public override string ToString() => $"{Name} (PID {Id})"; }
    private void RefreshProcesses()
    {
        var selected = (_processes.SelectedItem as ProcessChoice)?.Id;
        _processes.Items.Clear();
        foreach (var process in Process.GetProcessesByName("Wow"))
        {
            using (process) { try { _processes.Items.Add(new ProcessChoice(process.Id, process.ProcessName)); } catch (InvalidOperationException) { } }
        }
        for (int i = 0; i < _processes.Items.Count; i++)
            if (_processes.Items[i] is ProcessChoice item && item.Id == selected) _processes.SelectedIndex = i;
        if (_processes.SelectedIndex < 0 && _processes.Items.Count == 1) _processes.SelectedIndex = 0;
    }
    private void RefreshRoutes()
    {
        var selected = _routes.SelectedItem as string; _routes.Items.Clear();
        foreach (string route in _store.List()) _routes.Items.Add(route);
        if (selected != null && _routes.Items.Contains(selected)) _routes.SelectedItem = selected;
    }
    private void Attach()
    {
        if (_recorder?.IsRecording == true) throw new InvalidOperationException("Сначала завершите запись маршрута.");
        if (_stopping || _controller.IsRunning || _launch is { IsCompleted: false } || _worker is { IsCompleted: false }) throw new InvalidOperationException("Сначала остановите сбор/запуск.");
        var choice = _processes.SelectedItem as ProcessChoice ?? throw new InvalidOperationException("Выберите запущенный Wow.exe.");
        using var dialog = new OpenFileDialog { Title = "Профиль памяти клиента", Filter = "JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var profile = JsonSerializer.Deserialize<ClientProfile>(File.ReadAllText(dialog.FileName), JsonFiles.Options) ?? throw new FormatException("Пустой профиль");
        var memory = WindowsProcessMemory.Attach(choice.Id, profile);
        try
        {
            var position = new MemoryPositionProvider(memory, profile);
            var p = position.GetPosition(); // Проверка до замены старого подключения.
            IGameAdapter game = new UnavailableGameAdapter();
            if (profile.ObjectManager != null)
            {
                var entries = new GathererImporter().Parse(File.ReadAllText(_config.GathererFile))
                    .Where(n => n.Type == "HERB" && n.Entry.HasValue).Select(n => n.Entry!.Value).ToHashSet();
                if (entries.Count == 0) throw new FormatException("В Gatherer нет числовых HERB entry.");
                game = new MemoryGameAdapter(position, profile, new MemoryObjectScanner(memory, position, profile.ObjectManager, entries), _config.SearchRadius);
            }
            var controller = new BotController(game, MapProviderFactory.Create(_config, log: Log), _store, _config, Log);
            _memory?.Dispose(); _memory = memory; _position = position; _game = game; _controller = controller;
            _recorder = new RouteRecorder(position, _store, _hotkey);
            _status.Text = $"PID {choice.Id}: карта {p.MapId}, {p.X:F2}, {p.Y:F2}, {p.Z:F2}";
            Log("Память подключена: позиция и ObjectManager по профилю. Для реального сбора нужен IGameCommandBackend и backend навигации.");
        }
        catch { memory.Dispose(); throw; }
    }
    private void StartRecording()
    {
        if (_recorder == null || _position == null) throw new InvalidOperationException("Сначала подключите клиент и профиль памяти.");
        _position.GetPosition();
        _hotkey.Register(Handle);
        try { _recorder.Start(_routeName.Text.Trim()); } catch { _hotkey.Dispose(); throw; }
        Log("Запись начата: нажимайте R в игре или другом окне.");
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam == RegisteredRecordHotkey.Id) _hotkey.Notify();
        base.WndProc(ref m);
    }
    private void PollRecording()
    {
        if (_position != null)
        {
            try { var p = _position.GetPosition(); _status.Text = $"{_controller.State}: {p.X:F2}, {p.Y:F2}, {p.Z:F2}; mapId={p.MapId}"; }
            catch (Exception ex) { _status.Text = "Позиция недоступна: " + ex.Message; }
        }
        if (_recorder?.IsRecording != true) return;
        try
        {
            int before = _recorder.Count; _recorder.Poll();
            if (_recorder.Count != before) { _status.Text = $"Запись: {_recorder.Count} точек"; Log($"Добавлена точка {_recorder.Count}."); }
        }
        catch (Exception ex) { _status.Text = "Ошибка чтения позиции"; Log("Точка не добавлена: " + ex.Message); }
    }
    private void StopRecording()
    {
        if (_recorder == null) throw new InvalidOperationException("Запись не запущена.");
        var route = _recorder.Stop(); _hotkey.Dispose(); RefreshRoutes();
        if (route.Points.Count > 0) _routes.SelectedItem = route.Name;
        Log(route.Points.Count == 0 ? "Пустая запись завершена." : $"Сохранён маршрут {route.Name}, точек: {route.Points.Count}.");
    }
    private void ImportGatherer()
    {
        string name = _controller.ImportGatherer(_zone.Text.Trim()); RefreshRoutes(); _routes.SelectedItem = name;
        Log("Маршрут импортирован: " + name);
    }
    private async Task StartAsync()
    {
        if (_stopping) { Log("Дождитесь завершения остановки."); return; }
        if (_worker is { IsCompleted: false }) { Log("Сбор уже запущен."); return; }
        if (_routes.SelectedItem is not string name) { Log("Выберите маршрут."); return; }
        _start.Enabled = false; _cancelStart = false;
        try
        {
            // Построение пути и сбор выполняются вне UI, чтобы стоп и запись R оставались доступны.
            _launch = Task.Run(() => _controller.Start(name));
            await _launch;
            if (_cancelStart) { _controller.Stop(); return; }
            _worker = Task.Run(async () => { while (_controller.IsRunning) { _controller.Tick(); await Task.Delay(100); } });
            Log("Сбор запущен.");
        }
        catch (Exception ex) { Log("Ошибка запуска: " + ex.Message); }
        finally { _start.Enabled = !_stopping; }
    }
    private async Task StopAsync()
    {
        if (_stopping) return;
        _stopping = true; _stop.Enabled = false; _start.Enabled = false;
        try { _cancelStart = true; _controller.RequestStop(); if (_launch != null) { try { await _launch; } catch { } } if (_worker != null) await _worker; _controller.Stop(); if (_recorder?.IsRecording == true) StopRecording(); _hotkey.Dispose(); _memory?.Dispose(); _memory = null; _position = null; _recorder = null; _game = new UnavailableGameAdapter(); _controller = new(_game, MapProviderFactory.Create(_config, log: Log), _store, _config, Log); Log("Остановлено; прогресс сохранён, если маршрут был выбран."); }
        catch (Exception ex) { Log("Ошибка остановки: " + ex.Message); }
        finally { _stopping = false; _stop.Enabled = true; _start.Enabled = true; }
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        // Сохраняем точки до освобождения памяти. При ошибке оставляем окно открытым для повторной записи.
        try { if (_recorder?.IsRecording == true) StopRecording(); }
        catch (Exception ex) { Log("Не удалось сохранить запись: " + ex.Message); return; }
        await StopAsync(); _timer.Stop(); _hotkey.Dispose(); _memory?.Dispose(); _closing = true; Close();
    }
}
