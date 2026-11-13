using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;

namespace Sentravane.Sensor.Service.Native;

public static class IpHelperNative
{
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public int dwOwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved = 0);

    public record SocketConnection(int ProcessId, IPAddress LocalAddress, int LocalPort, IPAddress RemoteAddress, int RemotePort, uint State);

    public static List<SocketConnection> GetActiveTcpConnections()
    {
        var connections = new List<SocketConnection>();
        int bufferSize = 0;

        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL);
        if (bufferSize <= 0) return connections;

        IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            uint ret = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL);
            if (ret != 0) return connections;

            int numEntries = Marshal.ReadInt32(tcpTablePtr);
            IntPtr rowPtr = IntPtr.Add(tcpTablePtr, 4);

            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                var localAddr = new IPAddress(row.dwLocalAddr);
                var remoteAddr = new IPAddress(row.dwRemoteAddr);
                int localPort = (int)(((row.dwLocalPort & 0xFF) << 8) | ((row.dwLocalPort & 0xFF00) >> 8));
                int remotePort = (int)(((row.dwRemotePort & 0xFF) << 8) | ((row.dwRemotePort & 0xFF00) >> 8));

                connections.Add(new SocketConnection(row.dwOwningPid, localAddr, localPort, remoteAddr, remotePort, row.dwState));
                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tcpTablePtr);
        }

        return connections;
    }
}
