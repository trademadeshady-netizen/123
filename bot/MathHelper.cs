namespace HerbBot.Math;

/// <summary>
/// Абстракция проверки столкновений. В реальном проекте её реализует navmesh/collision-движок,
/// здесь мы лишь имитируем логику: бот не должен знать, КАК именно считается препятствие.
/// </summary>
public interface ICollisionProvider
{
    /// <returns>true, если на отрезке from-to есть препятствие.</returns>
    bool HasObstacle(Vector3 from, Vector3 to);
}

/// <summary>Заглушка по умолчанию: мир пустой, стен нет.</summary>
public sealed class EmptyWorldCollision : ICollisionProvider
{
    public bool HasObstacle(Vector3 from, Vector3 to) => false;
}

public static class MathHelper
{
    /// <summary>Подменяется в тестах/симуляторе. Статическое поле - чтобы сигнатура TraceLine осталась как в ТЗ.</summary>
    public static ICollisionProvider Collision { get; set; } = new UnavailableCollision();

    /// <summary>Имитация луча. true = на пути есть стена.</summary>
    public static bool TraceLine(Vector3 from, Vector3 to)
    {
        // Ошибка источника столкновений не доказывает, что путь свободен.
        // Передаём её вызывающему коду, чтобы тот остановил движение.
        return Collision.HasObstacle(from, to);
    }

    /// <summary>Угол (радианы, 0..2π) направления от from к to в горизонтальной плоскости.</summary>
    public static float TargetFacingToRadian(Vector3 from, Vector3 to)
    {
        float angle = MathF.Atan2(to.Y - from.Y, to.X - from.X);
        return NormalizeAngle(angle);
    }

    /// <summary>Приводит угол к диапазону [0, 2π).</summary>
    public static float NormalizeAngle(float a)
    {
        const float twoPi = MathF.PI * 2f;
        a %= twoPi;
        return a < 0 ? a + twoPi : a;
    }

    /// <summary>
    /// Кратчайшая угловая разница между двумя направлениями, диапазон [0, π].
    /// Именно она нужна для веса цели: разворот на 350° на деле - всего 10°.
    /// </summary>
    public static float AngleDifference(float a, float b)
    {
        float d = MathF.Abs(NormalizeAngle(a) - NormalizeAngle(b));
        return d > MathF.PI ? 2f * MathF.PI - d : d;
    }

    public static float DegToRad(float deg) => deg * MathF.PI / 180f;
}

/// <summary>Отсутствие карт не означает отсутствие препятствий.</summary>
public sealed class UnavailableCollision : ICollisionProvider
{
    public bool HasObstacle(Vector3 from, Vector3 to) => throw new InvalidOperationException("Источник столкновений не подключён.");
}
