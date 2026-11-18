namespace HerbBot.States;

public abstract class State
{
    public abstract string Name { get; }
    public virtual int Priority => 0;
    public abstract bool NeedToRun();
    public abstract void Run();
}

/// <summary>Простейший движок: на каждом тике берёт состояние с наивысшим приоритетом, которому нужно работать.</summary>
public sealed class StateMachine
{
    private readonly List<State> _states = new();
    public void Add(State s) { _states.Add(s); _states.Sort((a, b) => b.Priority.CompareTo(a.Priority)); }

    public void Pulse()
    {
        foreach (var s in _states)
        {
            try
            {
                if (!s.NeedToRun()) continue;
                s.Run();
                return;
            }
            catch (Exception ex)
            {
                // Ошибка одного состояния не должна убивать цикл: логируем и ждём следующий тик.
                Console.WriteLine($"[{s.Name}] ошибка: {ex.Message}");
                return;
            }
        }
    }
}
