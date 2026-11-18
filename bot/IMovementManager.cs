using HerbBot.Math;
namespace HerbBot.Interfaces;

public interface IMovementManager
{
    /// <summary>Один "тик" движения по пути. Вызывается из цикла задачи многократно.</summary>
    void Go(List<Vector3> path, bool stopWhenReach);
    void StopMove();
    bool IsStuck();
}

/// <summary>Низкоуровневый ввод (W/A/S/D или ClickToMove). Реализация - вне нашего проекта.</summary>
public interface IGameInput
{
    void ClickToMove(Vector3 target);
    void Stop();
    void Jump();
    void MoveBackward(int ms);
}

/// <summary>Игровые действия, не связанные с движением.</summary>
public interface IGameActions
{
    void Dismount();
    void InteractGameObject(ulong guid);
    bool IsCasting { get; }
    /// <summary>Суммарное число собранных трав в инвентаре (для проверки "предмет добавился").</summary>
    int GetHerbCount();
}
