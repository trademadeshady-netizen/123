using HerbBot.Infrastructure;
using HerbBot.Interfaces;
using HerbBot.Math;
namespace HerbBot.States;

public sealed class FarmingState : State
{
    private readonly BotContext _ctx;
    private readonly FarmingTask _task;

    public FarmingState(BotContext ctx) { _ctx = ctx; _task = new FarmingTask(ctx); }

    public override string Name => "Farming";

    public override bool NeedToRun()
    {
        try
        {
            var me = _ctx.Objects.GetLocalPlayer();
            if (me.IsDead) return false;
            if (Conditions.IsAttackedAndCannotIgnore(me)) return false;
            if (_ctx.IsPaused) return false;
            if (_ctx.LongMove.IsActive && !_ctx.Settings.HarvestDuringLongMove) return false;
            return true;
        }
        catch { return false; } // нет данных об игроке (загрузочный экран) - просто ничего не делаем
    }

    public override void Run()
    {
        try
        {
            var me = _ctx.Objects.GetLocalPlayer();
            var best = PickBest(me);
            if (best == null) return;                   // трав нет - вернёмся на следующем тике
            _task.Pulse(best);
        }
        catch (Exception ex)
        {
            // Трава исчезла из памяти и т.п. - не падаем, просто вернёмся к поиску цели.
            Console.WriteLine($"[Farming] сбой, возвращаюсь к поиску: {ex.Message}");
            _ctx.Movement.StopMove();
        }
    }

    private WoWGameObject? PickBest(LocalPlayer me)
    {
        var s = _ctx.Settings;
        WoWGameObject? best = null;
        double bestWeight = double.MaxValue;

        foreach (var node in _ctx.Objects.GetWoWGameObjectForFarm())
        {
            try
            {
                float dist = me.Position.DistanceTo(node.Position);
                if (dist > s.SearchRadiusObjects) continue;
                if (_ctx.Blacklist.IsBlacklisted(node.Guid)) continue;
                if (s.IgnoreIfPlayerNear && _ctx.Objects.IsOtherPlayerNear(node.Position, s.PlayerNearRadius)) continue;

                // Угол до цели считаем по кратчайшей дуге (0..π): штрафуем развороты.
                float want = MathHelper.TargetFacingToRadian(me.Position, node.Position);
                float turn = MathHelper.AngleDifference(me.Heading, want);

                // Weight = дистанция * (1 + 0.35 * угол/π) * случайный множитель 1.0-1.25.
                // Случайность ломает идеально повторяющиеся маршруты.
                double weight = dist * (1.0 + 0.35 * (turn / MathF.PI)) * Humanizer.Range(1.0, 1.25);
                if (weight < bestWeight) { bestWeight = weight; best = node; }
            }
            catch { /* битый объект пропускаем, остальные оцениваем дальше */ }
        }
        return best;
    }
}
