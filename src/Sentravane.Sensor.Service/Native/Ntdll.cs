using System.Runtime.InteropServices;

namespace Sentravane.Sensor.Service.Native;

public static class Ntdll
{
    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtResumeProcess(IntPtr processHandle);
}

