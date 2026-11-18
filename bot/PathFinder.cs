using HerbBot.Interfaces;
using HerbBot.Math;
namespace HerbBot.Navigation;

/// <summary>Поиск с ограниченным веером лучей. При неудаче запрещает движение по непроверенному пути.</summary>
public sealed class PathFinder : IPathFinder
{
    private const int MaxDepth = 8;
    private const int MaxProbes = 512;
    private const float ProbeLength = 15f;

    public List<Vector3> FindPath(Vector3 start, Vector3 end)
    {
        int probes = 0;
        var path = new List<Vector3> { start };
        if (Find(start, end, path, 0, ref probes)) return path;
        throw new InvalidOperationException("Не удалось построить свободный наземный маршрут.");
    }

    private static bool Find(Vector3 start, Vector3 end, List<Vector3> path, int depth, ref int probes)
    {
        if (++probes > MaxProbes) return false;
        if (!MathHelper.TraceLine(start, end)) { path.Add(end); return true; }
        if (depth >= MaxDepth) return false;

        float heading = MathHelper.TargetFacingToRadian(start, end);
        float length = MathF.Min(start.DistanceTo2D(end), ProbeLength);
        if (length < 0.01f) return false;
        // Если первый свободный луч ведёт в тупик, проверяем следующие лучи.
        for (int deg = -45; deg <= 45 && probes < MaxProbes; deg += 15)
        {
            Vector3 candidate = start.InFrontOf(heading + MathHelper.DegToRad(deg), length);
            if (++probes > MaxProbes || MathHelper.TraceLine(start, candidate)) continue;
            path.Add(candidate);
            if (Find(candidate, end, path, depth + 1, ref probes)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    public List<Vector3> FindFlyRoute(Vector3 start, Vector3 end)
    {
        var s = start.Clone(); s.SetFlying();
        var e = end.Clone(); e.SetFlying();
        if (!MathHelper.TraceLine(s, e)) return new() { s, e };
        for (float h = 10; h <= 30; h += 10)
        {
            var up = new Vector3(s.X, s.Y, s.Z + h, "Flying");
            var top = new Vector3(e.X, e.Y, e.Z + h, "Flying");
            // Проверяем также спуск: иначе найденный путь может оканчиваться внутри стены.
            if (!MathHelper.TraceLine(s, up) && !MathHelper.TraceLine(up, top) && !MathHelper.TraceLine(top, e))
                return new() { s, up, top, e };
        }
        throw new InvalidOperationException("Не удалось построить свободный воздушный маршрут.");
    }
}
