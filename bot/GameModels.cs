using HerbBot.Math;

namespace HerbBot.Interfaces;

/// <summary>Игровой объект (трава/жила).</summary>
public sealed class WoWGameObject
{
    public ulong Guid { get; set; }
    public int Entry { get; set; }
    public string Name { get; set; } = "";
    public Vector3 Position { get; set; } = new(0, 0, 0);
    /// <summary>Дистанция, с которой можно взаимодействовать.</summary>
    public float InteractDistance { get; set; } = 4.5f;
}

/// <summary>Наш персонаж. Heading и Speed добавлены сверх ТЗ: без них нельзя посчитать вес по углу и таймаут.</summary>
public sealed class LocalPlayer
{
    public Vector3 Position { get; set; } = new(0, 0, 0);
    public bool IsFlying { get; set; }
    public bool IsMounted { get; set; }
    public bool InCombat { get; set; }
    public int Health { get; set; } = 1;
    public int MaxHealth { get; set; } = 1;
    public bool IsMoving { get; set; }
    public float Heading { get; set; }          // радианы, как в MathHelper
    public float Speed { get; set; } = 7f;      // м/с

    public bool IsDead => Health <= 0;
}
