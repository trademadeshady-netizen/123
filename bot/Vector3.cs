namespace HerbBot.Math;

/// <summary>
/// Точка в 3D-мире. Структура копируется по значению, поэтому изменение типа
/// одной точки не меняет остальные точки маршрута.
/// </summary>
public struct Vector3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>"Ground", "Flying" или "Swimming" - определяет, КАК движемся к точке.</summary>
    public string Type { get; set; } = "Ground";

    public Vector3(float x, float y, float z, string type = "Ground")
    {
        X = x; Y = y; Z = z; Type = type;
    }

    /// <summary>Полное 3D-расстояние (теорема Пифагора в трёх измерениях).</summary>
    public float DistanceTo(Vector3 other)
    {
        float dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// Расстояние только по горизонтали. Нужно, потому что при полёте над травой
    /// мы хотим знать "насколько мы над ней по карте", не путая это с высотой.
    /// </summary>
    public float DistanceTo2D(Vector3 other)
    {
        float dx = X - other.X, dy = Y - other.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Разница высот по модулю (используется в условии DistanceZ > 4).</summary>
    public float DistanceZ(Vector3 other) => MathF.Abs(Z - other.Z);

    public bool IsFlying() => Type == "Flying";
    public bool IsGround() => Type == "Ground";

    public void SetFlying() => Type = "Flying";
    public void SetGround() => Type = "Ground";

    /// <summary>
    /// Точка на расстоянии distance от текущей в направлении heading (радианы).
    /// Конвенция: heading = 0 смотрит вдоль +X, рост угла - против часовой стрелки.
    /// Высоту сохраняем: "перед лицом" - это горизонтальное смещение.
    /// </summary>
    public Vector3 InFrontOf(float heading, float distance)
    {
        return new Vector3(
            X + MathF.Cos(heading) * distance,
            Y + MathF.Sin(heading) * distance,
            Z,
            Type);
    }

    public Vector3 Clone() => new(X, Y, Z, Type);

    public override string ToString() => $"({X:F1}; {Y:F1}; {Z:F1}) [{Type}]";
}
