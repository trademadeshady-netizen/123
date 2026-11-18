using System.Runtime.InteropServices;
namespace HerbBot.Routes;

public interface IKeyStateProvider { bool IsRDown(); }
/// <summary>Фронт нажатия убирает повторы при удержании R, не прекращая запись маршрута.</summary>
public sealed class PollingRecordHotkey(IKeyStateProvider keys) : IRecordHotkey
{
    private bool _wasDown;
    public bool TakeRecordPress()
    {
        bool down = keys.IsRDown(); bool pressed = down && !_wasDown; _wasDown = down; return pressed;
    }
}
/// <summary>Опрос состояния Windows не забирает клавишу у клиента, в отличие от регистрации R как hotkey.</summary>
public sealed class WindowsKeyStateProvider : IKeyStateProvider
{
    public bool IsRDown()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Глобальный опрос R требует Windows.");
        return (GetAsyncKeyState(0x52) & 0x8000) != 0;
    }
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
