using HerbBot.Interfaces;
using HerbBot.Math;
namespace HerbBot.Navigation;

public sealed class PathFinder : IPathFinder
{
    private const int MaxDepth = 8;          // защита от бесконечной рекурсии обхода
    private const float ProbeLength = 15f;   // длина пробного луча веера

    public List<Vector3> FindPath(Vector3 start, Vector3 end) => Find(start, end, 0);

    private List<Vector3> Find(Vector3 start, Vector3 end, int depth)
    {
        // Прямой путь свободен - возвращаем две точки.
        if (!MathHelper.TraceLine(start, end) || depth >= MaxDepth)
            return new List<Vector3> { start, end };

        float baseHeading = MathHelper.TargetFacingToRadian(start, end);
        float probe = MathF.Min(start.DistanceTo2D(end), ProbeLength);

        // Веер лучей от -45° до +45° с шагом 15°. Берём ПЕРВЫЙ свободный: порядок даёт предсказуемость.
        for (int deg = -45; deg <= 45; deg += 15)
        {
            float h = baseHeading + MathHelper.DegToRad(deg);
            Vector3 candidate = start.InFrontOf(h, probe);
            if (MathHelper.TraceLine(start, candidate)) continue;

            var rest = Find(candidate, end, depth + 1);
            var path = new List<Vector3> { start };
            path.AddRange(rest); // rest[0] == candidate
            return path;
        }

        // Ни один луч не свободен: отдаём прямой путь, дальше работает IsStuck/Unstuck.
        return new List<Vector3> { start, end };
    }

    public List<Vector3> FindFlyRoute(Vector3 start, Vector3 end)
    {
        var s = start.Clone(); s.SetFlying();
        var e = end.Clone(); e.SetFlying();
        if (!MathHelper.TraceLine(s, e)) return new List<Vector3> { s, e };

        // Пробуем подняться на 10/20/30 м и лететь поверху.
        for (float h = 10f; h <= 30f; h += 10f)
        {
            var up = new Vector3(s.X, s.Y, s.Z + h, "Flying");
            var top = new Vector3(e.X, e.Y, e.Z + h, "Flying");
            if (!MathHelper.TraceLine(s, up) && !MathHelper.TraceLine(up, top))
                return new List<Vector3> { s, up, top, e };
        }
        return new List<Vector3> { s, e };
    }
}
