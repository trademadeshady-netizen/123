using System.Diagnostics;
using HerbBot.Interfaces;
using HerbBot.Math;
using HerbBot.Memory;
using HerbBot.Objects;
using HerbBot.Routes;
namespace HerbBot.Runtime;

/// <summary>Единственная точка подключения SendInput/аддона/API. Реализация должна иметь собственные таймауты.
/// Чтение инвентаря, кастов и других игроков также нельзя заменять вымышленными значениями.</summary>
public interface IGameCommandBackend
{
    void PrepareTravel(bool flying);
    void MoveTo(Vector3 target, MoveType type);
    void StopMovement();
    void TargetObject(ulong guid);
    void InteractObject(ulong guid);
    bool IsLootWindowOpen();
    void LootAll();
    void SetMounted(bool mounted);
    bool IsCasting { get; }
    int GetHerbCount();
    bool IsOtherPlayerNear(Vector3 point, float radius);
    void Jump();
    void MoveBackward(int milliseconds);
}

/// <summary>Явное включение расширенного протокола таргета/лута; прежние IGameActions остаются совместимыми.</summary>
public interface IExplicitLootAdapter { }

/// <summary>Память используется только для наблюдения. Владельцем memory остаётся UI, освобождающий его после остановки worker.</summary>
public sealed class MemoryGameAdapter(MemoryPositionProvider position, MemoryProfile profile, IObjectScanner scanner, float radius, IGameCommandBackend? commands = null)
    : IGameAdapter, IExplicitLootAdapter, IObjectManager, IGameInput, IGameActions
{
    private IGameCommandBackend Commands => commands ?? throw new NotSupportedException("Игровой ввод не подключён. Реализуйте IGameCommandBackend через SendInput, аддон или API бота; чтение памяти не выполняет игровые действия.");
    public void Initialize(string gameVersion)
    {
        if (gameVersion != profile.GameVersion) throw new InvalidOperationException("Версия адаптера не совпадает с конфигом.");
        GetLocalPlayer();
    }
    public IObjectManager Objects => this;
    public IGameInput Input => this;
    public IGameActions Actions => this;
    public RoutePoint GetPosition() => position.GetPosition();
    public LocalPlayer GetLocalPlayer()
    {
        var snapshot = position.GetSnapshot();
        int health = position.ReadInt(profile.Health), max = position.ReadInt(profile.MaxHealth);
        float speed = position.ReadFloat(profile.Speed);
        if (health < 0 || max <= 0 || health > max || !float.IsFinite(speed) || speed < 0 || speed > 1000)
            throw new IOException("Недостоверные Health/MaxHealth/Speed в профиле.");
        return new() { Position = snapshot.Position.Position, Heading = snapshot.Rotation,
            Health = snapshot.IsGhost ? 0 : health, MaxHealth = max, Speed = speed,
            IsMounted = snapshot.IsMounted, IsFlying = snapshot.IsFlying, InCombat = snapshot.IsInCombat };
    }
    public IReadOnlyList<WoWGameObject> GetWoWGameObjectForFarm() => scanner.GetVisibleHerbs(radius)
        .Select(n => new WoWGameObject { Guid = n.Guid, Entry = n.Entry, Name = n.Name ?? $"Entry:{n.Entry}", Position = n.Position }).ToArray();
    public WoWGameObject? GetWoWGameObjectByName(string name) => GetWoWGameObjectForFarm().FirstOrDefault(n => n.Name == name);
    public WoWGameObject? GetWoWGameObjectByEntry(int id) => GetWoWGameObjectForFarm().FirstOrDefault(n => n.Entry == id);
    public bool IsValid(ulong guid) => scanner.GetVisibleHerbs(radius).Any(n => n.Guid == guid);
    public bool IsOtherPlayerNear(Vector3 point, float distance) => Commands.IsOtherPlayerNear(point, distance);
    public void PrepareTravel(bool flying) => Commands.PrepareTravel(flying);
    public void MoveTo(Vector3 target, MoveType type) => Commands.MoveTo(target, type);
    public void StopMovement() => Commands.StopMovement();
    public void TargetObject(ulong guid) => Commands.TargetObject(guid);
    public void InteractObject(ulong guid) => Commands.InteractObject(guid);
    public bool IsLootWindowOpen() => Commands.IsLootWindowOpen();
    public void LootAll() => Commands.LootAll();
    public bool IsMounted() => position.GetSnapshot().IsMounted;
    public bool IsFlying() => position.GetSnapshot().IsFlying;
    public void SetMounted(bool mounted) => Commands.SetMounted(mounted);
    public void ClickToMove(Vector3 target) => MoveTo(target, IsFlying() ? MoveType.Flying : MoveType.Ground);
    public void Stop() => StopMovement();
    public void Jump() => Commands.Jump();
    public void MoveBackward(int milliseconds) => Commands.MoveBackward(milliseconds);
    public void Dismount() => SetMounted(false);
    public void InteractGameObject(ulong guid) => InteractObject(guid);
    public bool IsCasting => Commands.IsCasting;
    public int GetHerbCount() => Commands.GetHerbCount();
}

/// <summary>Ограниченное ожидание лута: удаление объекта до получения лута считается отказом, а не успехом.
/// При автолуте подтверждением служит реальное изменение инвентаря.</summary>
public static class GameInteraction
{
    public static bool Gather(IGameAdapter game, ulong guid, TimeSpan lootTimeout, Func<bool> canContinue, Func<bool> waitCasting)
    {
        if (lootTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lootTimeout));
        if (!canContinue() || !game.Objects.IsValid(guid)) return false;
        int before = game.Actions.GetHerbCount();
        game.TargetObject(guid);
        if (!canContinue() || !game.Objects.IsValid(guid)) return false;
        game.InteractObject(guid);
        if (!waitCasting()) return false;
        var timer = Stopwatch.StartNew(); bool lootRequested = false;
        while (timer.Elapsed < lootTimeout)
        {
            if (!canContinue()) return false;
            if (game.Actions.GetHerbCount() > before) return true;
            // После команды лута сервер может удалить объект раньше обновления инвентаря.
            if (!lootRequested && !game.Objects.IsValid(guid)) return false;
            if (!lootRequested && game.IsLootWindowOpen()) { game.LootAll(); lootRequested = true; }
            Thread.Sleep(25);
        }
        throw new TimeoutException($"Лут GUID {guid:X} не подтверждён инвентарём за {lootTimeout.TotalSeconds:F2} с.");
    }
}
