using System.ComponentModel;
using System.Runtime.InteropServices;
using HerbBot.Routes;
namespace HerbBot.WinForms;

/// <summary>Регистрация уровня ОС получает R при фокусе WoW; при конфликте показываем ошибку.</summary>
internal sealed class RegisteredRecordHotkey : IRecordHotkey, IDisposable
{
    public const int Id = 0x4842;
    private nint _window;
    private int _pending;
    public void Register(nint window)
    {
        if (_window != 0) return;
        if (!RegisterHotKey(window, Id, 0x4000, 0x52)) throw new Win32Exception(Marshal.GetLastWin32Error(), "R занята другим приложением.");
        _window = window;
    }
    public void Notify() => Interlocked.Increment(ref _pending);
    public bool TakeRecordPress()
    {
        if (_pending == 0) return false;
        Interlocked.Decrement(ref _pending); return true;
    }
    public void Dispose() { if (_window != 0) UnregisterHotKey(_window, Id); _window = 0; _pending = 0; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
