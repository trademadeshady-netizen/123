using System.Diagnostics;
using HerbBot.Interfaces;
using HerbBot.Math;
namespace HerbBot.Navigation;

public sealed class MovementManager : IMovementManager
{
    private readonly IGameInput _input;
    private readonly IObjectManager _objects;
    private int _wp;
    private List<Vector3>? _lastPath;
    private Vector3? _lastPos;
    private long _lastMoveTime;
    private bool _moving;
    private const float WaypointRadius = 1.5f;

    private readonly double _stuckSeconds;
    public MovementManager(IGameInput input, IObjectManager objects, double stuckSeconds = 2) { _input = input; _objects = objects; _stuckSeconds = stuckSeconds; }

    public void Go(List<Vector3> path, bool stopWhenReach)
    {
        if (path.Count == 0) { StopMove(); return; }
        try
        {
            if (!ReferenceEquals(path, _lastPath))
            {
                _lastPath = path; _wp = path.Count > 1 ? 1 : 0;
                _lastPos = null; _moving = false;
            }
            var me = _objects.GetLocalPlayer().Position;
            // При полёте близость по XY без учёта высоты пропустит вертикальную точку.
            while (_wp < path.Count - 1 && me.DistanceTo(path[_wp]) < WaypointRadius) _wp++;
            if (_wp == path.Count - 1 && stopWhenReach && me.DistanceTo(path[_wp]) < WaypointRadius)
            { StopMove(); return; }
            if (!_moving) { _lastPos = me; _lastMoveTime = Stopwatch.GetTimestamp(); _moving = true; }
            _input.ClickToMove(path[_wp]);
        }
        catch { StopMove(); throw; }
    }

    public void StopMove()
    {
        _moving = false; _lastPos = null; _lastPath = null; _wp = 0;
        try { _input.Stop(); } catch { /* Состояние всё равно сброшено; вызывающий может продолжить поиск. */ }
    }

    public bool IsStuck()
    {
        if (!_moving) return false;
        var pos = _objects.GetLocalPlayer().Position;
        if (!_lastPos.HasValue || pos.DistanceTo(_lastPos.Value) > 0.3f)
        { _lastPos = pos; _lastMoveTime = Stopwatch.GetTimestamp(); return false; }
        if (Stopwatch.GetElapsedTime(_lastMoveTime) < TimeSpan.FromSeconds(_stuckSeconds)) return false;
        Unstuck();
        _lastPos = pos; _lastMoveTime = Stopwatch.GetTimestamp();
        return true;
    }

    private void Unstuck()
    {
        if (Infrastructure.Humanizer.Chance(0.5)) _input.Jump();
        else _input.MoveBackward((int)Infrastructure.Humanizer.Range(300, 700));
    }
}
