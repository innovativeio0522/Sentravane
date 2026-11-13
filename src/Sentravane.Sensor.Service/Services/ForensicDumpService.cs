using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Sentravane.Sensor.Service.Services;

public interface IForensicDumpService
{
    string? GenerateProcessMinidump(int pid, string incidentId);
}

public class ForensicDumpService : IForensicDumpService
{
    private readonly ILogger<ForensicDumpService> _logger;
    private readonly string _dumpDirectory;

    [Flags]
    public enum MINIDUMP_TYPE : uint
    {
        MiniDumpNormal = 0x00000000,
        MiniDumpWithDataSegs = 0x00000001,
        MiniDumpWithFullMemory = 0x00000002,
        MiniDumpWithHandleData = 0x00000004,
        MiniDumpFilterMemory = 0x00000008,
        MiniDumpScanMemory = 0x00000010,
        MiniDumpWithUnloadedModules = 0x00000020,
        MiniDumpWithIndirectlyReferencedMemory = 0x00000040,
        MiniDumpFilterModulePaths = 0x00000080,
        MiniDumpWithProcessThreadData = 0x00000100,
        MiniDumpWithPrivateReadWriteMemory = 0x00000200,
        MiniDumpWithoutOptionalData = 0x00000400,
        MiniDumpWithFullMemoryInfo = 0x00000800,
        MiniDumpWithThreadInfo = 0x00001000,
        MiniDumpWithCodeSegs = 0x00002000
    }

    [DllImport("dbghelp.dll", EntryPoint = "MiniDumpWriteDump", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint processId,
        SafeHandle hFile,
        uint dumpType,
        IntPtr expParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;

    public ForensicDumpService(ILogger<ForensicDumpService> logger)
    {
        _logger = logger;
        _dumpDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Sentravane", "Dumps");
        Directory.CreateDirectory(_dumpDirectory);
    }

    public string? GenerateProcessMinidump(int pid, string incidentId)
    {
        var dumpPath = Path.Combine(_dumpDirectory, $"incident_{incidentId}_pid_{pid}.dmp");
        _logger.LogInformation("Generating forensic minidump for PID {Pid} at {Path}", pid, dumpPath);

        IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, (uint)pid);
        if (hProcess == IntPtr.Zero)
        {
            _logger.LogWarning("Failed to open process {Pid} with VM_READ/QUERY_INFO for minidump generation. Error: {Error}", pid, Marshal.GetLastWin32Error());
            return null;
        }

        try
        {
            using var fileStream = new FileStream(dumpPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            bool success = MiniDumpWriteDump(
                hProcess,
                (uint)pid,
                fileStream.SafeFileHandle,
                (uint)(MINIDUMP_TYPE.MiniDumpNormal | MINIDUMP_TYPE.MiniDumpWithHandleData | MINIDUMP_TYPE.MiniDumpWithThreadInfo),
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (success)
            {
                _logger.LogInformation("Successfully captured forensic minidump at {Path} ({Size} bytes)", dumpPath, fileStream.Length);
                return dumpPath;
            }
            else
            {
                _logger.LogError("MiniDumpWriteDump failed for PID {Pid} with error code {Error}", pid, Marshal.GetLastWin32Error());
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception capturing minidump for PID {Pid}", pid);
            return null;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }
}
