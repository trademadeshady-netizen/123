using System.Diagnostics;
using HerbBot.Infrastructure;
using HerbBot.Interfaces;
using HerbBot.Math;
namespace HerbBot.States;

public sealed class FarmingTask
{
    private readonly BotContext _ctx;

    public FarmingTask(BotContext ctx) => _ctx = ctx;

    /// <returns>true, если инвентарь подтвердил сбор.</returns>
    public bool Pulse(WoWGameObject node)
    {
        try
        {
            if (!CanContinue(node.Guid)) return false;
            var me = _ctx.Objects.GetLocalPlayer();
            List<Vector3> path = me.IsFlying
                ? _ctx.LongMove.DecollideFlightPath(_ctx.PathFinder.FindFlyRoute(me.Position, node.Position))
                : _ctx.PathFinder.FindPath(me.Position, node.Position);
            if (path.Count < 2) throw new InvalidOperationException("Маршрут не содержит начальную и конечную точки.");
            AddSafeJitter(path);

            float speed = me.Speed > 0.1f ? me.Speed : 7f;
            var timeout = TimeSpan.FromSeconds(me.Position.DistanceTo(node.Position) / speed + _ctx.Settings.TravelTimeoutMarginSeconds);
            var timer = Stopwatch.StartNew(); // Монотонные часы не зависят от изменения системного времени.
            float reach = System.Math.Clamp(node.InteractDistance, 0.1f, 4.5f);
            while (true)
            {
                if (!CanContinue(node.Guid)) return false;
                me = _ctx.Objects.GetLocalPlayer();
                if (me.Position.DistanceTo2D(node.Position) <= reach && me.Position.DistanceZ(node.Position) <= 4f) break;
                if (timer.Elapsed >= timeout)
                {
                    _ctx.Blacklist.Add(node.Guid, _ctx.Settings.BlacklistDuration);
                    return false;
                }
                _ctx.Movement.Go(path, true);
                _ctx.Movement.IsStuck();
                Humanizer.Sleep(50, 120);
            }
            _ctx.Movement.StopMove();
            if (!CanContinue(node.Guid)) return false;
            _ctx.Actions.Dismount();
            Humanizer.Sleep(800, 1200);

            for (int attempt = 0; attempt < _ctx.Settings.GatherAttempts; attempt++)
            {
                if (!CanContinue(node.Guid)) return false;
                int before = _ctx.Actions.GetHerbCount();
                if (_ctx.Game != null)
                {
                    if (!Runtime.GameInteraction.Gather(_ctx.Game, node.Guid, TimeSpan.FromSeconds(_ctx.Settings.LootTimeoutSeconds), () => !Interrupted(), WaitIsCasting)) return false;
                }
                else
                {
                    _ctx.Actions.InteractGameObject(node.Guid);
                    if (!WaitIsCasting()) return false;
                }
                // Объект может исчезнуть после успешного сбора, поэтому сначала проверяем инвентарь.
                if (_ctx.Actions.GetHerbCount() > before)
                {
                    _ctx.Blacklist.Add(node.Guid, _ctx.Settings.BlacklistDuration);
                    return true;
                }
                if (attempt + 1 < _ctx.Settings.GatherAttempts) Humanizer.Sleep(300, 600);
            }
            _ctx.Blacklist.Add(node.Guid, _ctx.Settings.BlacklistDuration);
            return false;
        }
        catch (Exception ex)
        {
            // Снимок объекта или адаптер могут перестать быть доступными между двумя проверками.
            _ctx.Log($"[FarmingTask] {ex.GetType().Name}: {ex.Message}");
            _ctx.Blacklist.Add(node.Guid, TimeSpan.FromMinutes(1));
            return false;
        }
        finally
        {
            try { _ctx.Movement.StopMove(); } catch { }
        }
    }

    private bool CanContinue(ulong guid)
    {
        if (_ctx.IsPaused || (_ctx.LongMove.IsActive && !_ctx.Settings.HarvestDuringLongMove)) return false;
        var player = _ctx.Objects.GetLocalPlayer();
        return !player.IsDead && !Conditions.IsAttackedAndCannotIgnore(player) && _ctx.Objects.IsValid(guid);
    }

    private bool WaitIsCasting()
    {
        var timer = Stopwatch.StartNew();
        while (!_ctx.Actions.IsCasting && timer.Elapsed < TimeSpan.FromSeconds(1))
        {
            if (Interrupted()) return false;
            Thread.Sleep(50);
        }
        timer.Restart();
        while (_ctx.Actions.IsCasting && timer.Elapsed < TimeSpan.FromSeconds(4))
        {
            if (Interrupted()) return false;
            Thread.Sleep(50);
        }
        // Не начинаем следующую попытку, пока предыдущий каст ещё не закончился.
        return !_ctx.Actions.IsCasting && !Interrupted();
    }

    private bool Interrupted()
    {
        if (_ctx.IsPaused || (_ctx.LongMove.IsActive && !_ctx.Settings.HarvestDuringLongMove)) return true;
        var player = _ctx.Objects.GetLocalPlayer();
        return player.IsDead || Conditions.IsAttackedAndCannotIgnore(player);
    }

    private static void AddSafeJitter(List<Vector3> path)
    {
        // Даже на прямом маршруте добавляем промежуточную точку, но проверяем оба новых отрезка.
        if (path.Count == 2)
        {
            var a = path[0]; var b = path[1];
            var middle = new Vector3((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2, a.Type);
            var candidate = Humanizer.Jitter(middle);
            if (!MathHelper.TraceLine(a, candidate) && !MathHelper.TraceLine(candidate, b)) path.Insert(1, candidate);
            return;
        }
        for (int i = 1; i < path.Count - 1; i++)
        {
            var candidate = Humanizer.Jitter(path[i]);
            if (!MathHelper.TraceLine(path[i - 1], candidate) && !MathHelper.TraceLine(candidate, path[i + 1]))
                path[i] = candidate;
        }
    }
}
