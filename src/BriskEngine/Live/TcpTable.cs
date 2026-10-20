using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;

namespace BriskEngine.Live;

public sealed record TcpConnection(int Pid, IPAddress Remote, int RemotePort);

public interface ITcpTableProbe
{
    IReadOnlyList<TcpConnection> Established();
}

public sealed class RealTcpTableProbe : ITcpTableProbe
{
    private const int AfInet = 2, AfInet6 = 23, TableOwnerPidAll = 5;
    private const uint StateEstablished = 5, InsufficientBuffer = 122;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order,
        int af, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct Row4 { public uint State, LocalAddr, LocalPort, RemoteAddr, RemotePort, OwningPid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Row6
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
        public uint LocalScopeId, LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddr;
        public uint RemoteScopeId, RemotePort, State, OwningPid;
    }

    private static int Port(uint networkOrder) =>
        (int)(((networkOrder & 0xFF) << 8) | ((networkOrder >> 8) & 0xFF));

    public IReadOnlyList<TcpConnection> Established()
    {
        var list = new List<TcpConnection>();
        Read<Row4>(AfInet, list, r => r.State == StateEstablished
            ? new TcpConnection((int)r.OwningPid, new IPAddress(r.RemoteAddr), Port(r.RemotePort)) : null);
        Read<Row6>(AfInet6, list, r => r.State == StateEstablished
            ? new TcpConnection((int)r.OwningPid, new IPAddress(r.RemoteAddr, r.RemoteScopeId), Port(r.RemotePort)) : null);
        return list;
    }

    /// The table can grow between the size query and the read, so it retries.
    private static void Read<T>(int af, List<TcpConnection> into, Func<T, TcpConnection?> map) where T : struct
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TableOwnerPidAll, 0);
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var rc = GetExtendedTcpTable(buffer, ref size, false, af, TableOwnerPidAll, 0);
                if (rc == InsufficientBuffer) continue;
                if (rc != 0) return;
                var count = Marshal.ReadInt32(buffer);
                var row = buffer + 4;
                var rowSize = Marshal.SizeOf<T>();
                for (var i = 0; i < count; i++, row += rowSize)
                    if (map(Marshal.PtrToStructure<T>(row)) is { } c) into.Add(c);
                return;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
}
