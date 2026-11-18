using System.Collections.Concurrent;
namespace HerbBot.Infrastructure;

/// <summary>Чёрный список GUID с временем жизни. Потокобезопасный: им могут пользоваться и состояния, и UI.</summary>
public sealed class BlacklistManager
{
    private readonly ConcurrentDictionary<ulong, DateTime> _expires = new();

    public void Add(ulong guid, TimeSpan duration) => _expires[guid] = DateTime.UtcNow + duration;

    public bool IsBlacklisted(ulong guid)
    {
        if (!_expires.TryGetValue(guid, out var until)) return false;
        if (DateTime.UtcNow < until) return true;
        // Срок истёк - лениво удаляем, отдельный таймер не нужен.
        _expires.TryRemove(guid, out _);
        return false;
    }

    public void Cleanup()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _expires)
            if (kv.Value <= now) _expires.TryRemove(kv.Key, out _);
    }

    public void Clear() => _expires.Clear();
}
