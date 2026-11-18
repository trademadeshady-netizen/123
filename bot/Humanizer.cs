using HerbBot.Math;
namespace HerbBot.Infrastructure;

/// <summary>Рандомизация поведения, чтобы паузы и маршруты не были идеально повторяющимися.</summary>
public static class Humanizer
{
    private static readonly Random _rnd = new();
    private static readonly object _lock = new(); // Random не потокобезопасен

    public static void Sleep(int minMs, int maxMs)
    {
        int ms;
        lock (_lock) ms = _rnd.Next(minMs, maxMs + 1);
        Thread.Sleep(ms);
    }

    public static double Random01() { lock (_lock) return _rnd.NextDouble(); }

    public static bool Chance(double probability) => Random01() < probability;

    /// <summary>Случайное число в диапазоне [min, max].</summary>
    public static double Range(double min, double max) => min + Random01() * (max - min);

    /// <summary>Случайное горизонтальное отклонение точки до maxMeters (по ТЗ - до 1.5 м).</summary>
    public static Vector3 Jitter(Vector3 p, float maxMeters = 1.5f)
    {
        float angle = (float)(Random01() * System.Math.PI * 2);
        float dist = (float)(Random01() * maxMeters);
        return p.InFrontOf(angle, dist);
    }
}
