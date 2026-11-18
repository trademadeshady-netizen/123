using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
namespace HerbBot.Memory;

/// <summary>Только чтение: открываем процесс с PROCESS_VM_READ и QUERY_LIMITED_INFORMATION.</summary>
public sealed class WindowsProcessMemory : IReadOnlyProcessMemory
{
    private const uint ReadAccess = 0x0010, QueryAccess = 0x1000;
    private readonly SafeProcessHandle _handle;
    public ulong ModuleBase { get; }
    private WindowsProcessMemory(SafeProcessHandle handle, ulong moduleBase) { _handle = handle; ModuleBase = moduleBase; }

    public static WindowsProcessMemory Attach(int pid, MemoryProfile profile)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Чтение памяти клиента доступно только в Windows.");
        profile.Validate();
        using var process = Process.GetProcessById(pid);
        if (!string.Equals(process.ProcessName, "Wow", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Выбранный процесс не называется Wow.exe.");
        using var module = process.MainModule ?? throw new InvalidOperationException("Основной модуль клиента недоступен.");
        var path = module.FileName;
        var actualVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
        if (!string.Equals(actualVersion, profile.ClientFileVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Версия файла клиента ({actualVersion}) не совпадает с профилем.");
        using var file = File.OpenRead(path);
        using (var pe = new PEReader(file, PEStreamOptions.LeaveOpen))
            if (pe.PEHeaders.CoffHeader.Machine != System.Reflection.PortableExecutable.Machine.I386 || pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32)
                throw new InvalidOperationException("Профиль поддерживает только x86 PE32 клиент.");
        file.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(file));
        if (!string.Equals(hash, profile.ClientSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SHA-256 клиента не совпадает с профилем.");
        ulong baseAddress = checked((ulong)module.BaseAddress.ToInt64());
        if (baseAddress > uint.MaxValue) throw new InvalidOperationException("База модуля вне диапазона x86.");
        var handle = OpenProcess(ReadAccess | QueryAccess, false, pid);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            throw new Win32Exception(error, "Не удалось открыть клиент для чтения памяти.");
        }
        if (process.HasExited) { handle.Dispose(); throw new InvalidOperationException("Клиент уже завершился."); }
        return new WindowsProcessMemory(handle, baseAddress);
    }
    public void ReadExactly(ulong address, Span<byte> destination)
    {
        if (_handle.IsClosed || _handle.IsInvalid) throw new IOException("Процесс отключён.");
        if (!GetExitCodeProcess(_handle, out uint exitCode) || exitCode != 259) throw new IOException("Клиент завершён или недоступен.");
        if (address < 0x10000 || address > uint.MaxValue - (ulong)destination.Length) throw new IOException("Адрес вне диапазона клиента.");
        byte[] data = new byte[destination.Length];
        if (!ReadProcessMemory(_handle, (nuint)address, data, (nuint)data.Length, out var read))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Не удалось прочитать адрес 0x{address:X}.");
        if (read != (nuint)data.Length) throw new IOException($"Прочитано {read} из {data.Length} байт.");
        data.CopyTo(destination);
    }
    public void Dispose() => _handle.Dispose();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nuint address, [Out] byte[] buffer, nuint size, out nuint read);
}
