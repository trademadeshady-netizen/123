using HerbBot.Infrastructure;
using HerbBot.Interfaces;
using HerbBot.Math;
namespace HerbBot.States;

public sealed class FarmingTask
{
    private readonly BotContext _ctx;
    private const int MaxGatherAttempts = 3;

    public FarmingTask(BotContext ctx) => _ctx = ctx;

    /// <returns>true, если трава успешно собрана.</returns>
    public bool Pulse(WoWGameObject node)
    {
        var objects = _ctx.Objects;
        try
        {
            var me = objects.GetLocalPlayer();

            // 1. Строим маршрут (в полёте - отдельный алгоритм + обход препятствий в воздухе).
            List<Vector3> path;
            if (me.IsFlying)
            {
                path = _ctx.PathFinder.FindFlyRoute(me.Position, node.Position);
                path = _ctx.LongMove.DecollideFlightPath(path);
            }
            else path = _ctx.PathFinder.FindPath(me.Position, node.Position);

            // Человеческое отклонение до 1.5 м - только у промежуточных точек, финальную не трогаем.
            for (int i = 1; i < path.Count - 1; i++) path[i] = Humanizer.Jitter(path[i], 1.5f);

            // 2. Таймаут: время = дистанция / скорость + 5 секунд.
            float speed = me.Speed > 0.1f ? me.Speed : 7f;
            var timeout = TimeSpan.FromSeconds(me.Position.DistanceTo(node.Position) / speed + 5);
            var started = DateTime.UtcNow;

            // 3. Движение.
            while (true)
            {
                if (!objects.IsValid(node.Guid))      // траву забрали - не ошибка, просто выходим
                { _ctx.Movement.StopMove(); return false; }

                me = objects.GetLocalPlayer();
                if (me.IsDead || Conditions.IsAttackedAndCannotIgnore(me) || _ctx.IsPaused)
                { _ctx.Movement.StopMove(); return false; }

                float d2 = me.Position.DistanceTo2D(node.Position);
                float dz = me.Position.DistanceZ(node.Position);
                if (d2 <= 4.5f && dz <= 4f) break;    // дошли

                if (DateTime.UtcNow - started > timeout)
                {
                    // Не дошли вовремя: бросаем цель и банним, чтобы не залипнуть на ней.
                    _ctx.Movement.StopMove();
                    _ctx.Blacklist.Add(node.Guid, _ctx.Settings.BlacklistDuration);
                    return false;
                }

                _ctx.Movement.Go(path, true);
                _ctx.Movement.IsStuck();              // при застревании внутри выполнится Unstuck()
                Humanizer.Sleep(50, 120);
            }
            _ctx.Movement.StopMove();

            // 4. Сбор.
            _ctx.Actions.Dismount();
            Humanizer.Sleep(800, 1200); // случайная "реакция человека" вместо фиксированных 500 мс

            for (int attempt = 1; attempt <= MaxGatherAttempts; attempt++)
            {
                if (!objects.IsValid(node.Guid)) return false;

                int before = _ctx.Actions.GetHerbCount();
                _ctx.Actions.InteractGameObject(node.Guid);
                WaitIsCasting();

                if (_ctx.Actions.GetHerbCount() > before)
                {
                    _ctx.Blacklist.Add(node.Guid, _ctx.Settings.BlacklistDuration);
                    return true;
                }
                Humanizer.Sleep(300, 600);
            }

            // 3 неудачи: траву в бан, чтобы не крутиться вокруг неё вечно.
            _ctx.Blacklist.Add(node.Guid, _ctx.Settings.BlacklistDuration);
            return false;
        }
        catch (Exception ex)
        {
            // Объект пропал из памяти посреди выполнения и т.п.: гасим движение, баним GUID, возвращаемся в поиск.
            Console.WriteLine($"[FarmingTask] {ex.GetType().Name}: {ex.Message}");
            try { _ctx.Movement.StopMove(); } catch { }
            _ctx.Blacklist.Add(node.Guid, TimeSpan.FromMinutes(1));
            return false;
        }
    }

    /// <summary>Ждём начала и конца каста (~1.5-2 с). Верхние пределы защищают от вечного ожидания.</summary>
    private void WaitIsCasting()
    {
        var t0 = DateTime.UtcNow;
        while (!_ctx.Actions.IsCasting && (DateTime.UtcNow - t0).TotalMilliseconds < 1000) Thread.Sleep(50);
        t0 = DateTime.UtcNow;
        while (_ctx.Actions.IsCasting && (DateTime.UtcNow - t0).TotalMilliseconds < 4000) Thread.Sleep(50);
    }
}
