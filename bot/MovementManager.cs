using HerbBot.Interfaces;
using HerbBot.Math;
namespace HerbBot.Navigation;

public sealed class MovementManager : IMovementManager
{
    private readonly IGameInput _input;
    private readonly IObjectManager _objects;
    private int _wp;                       // индекс текущей путевой точки
    private List<Vector3>? _lastPath;
    private Vector3? _lastPos;
    private DateTime _lastMoveTime = DateTime.UtcNow;

    private const float WaypointRadius = 1.5f;
    private static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(2);

    public MovementManager(IGameInput input, IObjectManager objects)
    {
        _input = input; _objects = objects;
    }

    public void Go(List<Vector3> path, bool stopWhenReach)
    {
        try
        {
            if (path.Count == 0) return;
            // Новый путь - сбрасываем индекс; иначе продолжаем с текущей точки.
            if (!ReferenceEquals(path, _lastPath)) { _lastPath = path; _wp = path.Count > 1 ? 1 : 0; }

            var me = _objects.GetLocalPlayer().Position;
            while (_wp < path.Count - 1 && me.DistanceTo2D(path[_wp]) < WaypointRadius) _wp++;

            var target = path[_wp];
            bool last = _wp == path.Count - 1;
            if (last && stopWhenReach && me.DistanceTo2D(target) < WaypointRadius) { StopMove(); return; }

            _input.ClickToMove(target);
        }
        catch (Exception) { StopMove(); } // любая ошибка ввода - безопасная остановка, а не падение бота
    }

    public void StopMove()
    {
        try { _input.Stop(); } catch { /* остановка не должна бросать исключения */ }
    }

    /// <summary>Застрял: координаты не меняются больше 2 секунд, хотя мы должны двигаться.</summary>
    public bool IsStuck()
    {
        try
        {
            var pos = _objects.GetLocalPlayer().Position;
            if (_lastPos == null || pos.DistanceTo(_lastPos) > 0.3f)
            {
                _lastPos = pos.Clone();
                _lastMoveTime = DateTime.UtcNow;
                return false;
            }
            if (DateTime.UtcNow - _lastMoveTime > StuckAfter)
            {
                Unstuck();
                _lastMoveTime = DateTime.UtcNow;
                return true;
            }
        }
        catch { /* нет данных об игроке - считаем, что не застряли */ }
        return false;
    }

    private void Unstuck()
    {
        // Случайный выбор между прыжком и шагом назад - детерминированный повтор мог бы застрять снова.
        if (Infrastructure.Humanizer.Chance(0.5)) _input.Jump();
        else _input.MoveBackward((int)Infrastructure.Humanizer.Range(300, 700));
    }
}
