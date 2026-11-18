using HerbBot.Interfaces;
namespace HerbBot.Infrastructure;

public sealed class BotSettings
{
    public float SearchRadiusObjects { get; set; } = 100f;
    public bool IgnoreIfPlayerNear { get; set; } = true;
    public float PlayerNearRadius { get; set; } = 30f;
    public bool HarvestDuringLongMove { get; set; } = false;
    public TimeSpan BlacklistDuration { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>Общий контекст: все сервисы в одном месте, состояния получают его через конструктор (простой DI).</summary>
public sealed class BotContext
{
    public required IObjectManager Objects { get; init; }
    public required IPathFinder PathFinder { get; init; }
    public required IMovementManager Movement { get; init; }
    public required IGameActions Actions { get; init; }
    public BlacklistManager Blacklist { get; } = new();
    public BotSettings Settings { get; } = new();
    public LongMove LongMove { get; } = new();
    public volatile bool IsPaused;
}

/// <summary>Флаг "идёт длительное перемещение" + обход препятствий в воздухе.</summary>
public sealed class LongMove
{
    public bool IsActive { get; set; }

    /// <summary>
    /// Поднимает путь, если сегмент упирается в препятствие. Вставляем промежуточную
    /// точку выше, а не сдвигаем в сторону: в полёте набор высоты - самый дешёвый манёвр.
    /// </summary>
    public List<Math.Vector3> DecollideFlightPath(List<Math.Vector3> path, float lift = 15f)
    {
        var result = new List<Math.Vector3>();
        for (int i = 0; i < path.Count; i++)
        {
            result.Add(path[i]);
            if (i == path.Count - 1) break;
            var a = path[i]; var b = path[i + 1];
            if (Math.MathHelper.TraceLine(a, b))
            {
                var mid = new Math.Vector3((a.X + b.X) / 2, (a.Y + b.Y) / 2, System.Math.Max(a.Z, b.Z) + lift, "Flying");
                result.Add(mid);
            }
        }
        return result;
    }
}
