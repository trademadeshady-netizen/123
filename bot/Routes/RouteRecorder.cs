namespace HerbBot.Routes;

public interface IPlayerPositionProvider { RoutePoint GetPosition(); }
/// <summary>Событие нажатия, а не уровень удержания: одно нажатие даёт одну точку.</summary>
public interface IRecordHotkey { bool TakeRecordPress(); }

public sealed class RouteRecorder(IPlayerPositionProvider positions, RouteStore store, IRecordHotkey hotkey)
{
    private readonly List<RoutePoint> _points = [];
    private string? _name;
    public bool IsRecording => _name != null;
    public int Count => _points.Count;
    public void Start(string name)
    {
        if (IsRecording) throw new InvalidOperationException("Запись уже идёт");
        RouteStore.ValidateName(name);
        _name = name; _points.Clear();
    }
    public void Poll()
    {
        if (hotkey.TakeRecordPress() && IsRecording) _points.Add(positions.GetPosition() with { Timestamp = DateTimeOffset.UtcNow });
    }
    public HerbRoute Stop()
    {
        if (_name == null) throw new InvalidOperationException("Запись не запущена");
        var route = new HerbRoute(1, _name, [.. _points]);
        if (_points.Count > 0) store.Save(route);
        _name = null; return route; // При ошибке записи точки остаются в памяти для повтора.
    }
}
