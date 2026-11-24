using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace StockCount.Server.Hosting;

public sealed record PortOwner(int Port, int ProcessId, string Name, string? Path, long? StartedUtcTicks)
{
    public bool IsStockCount => Name.Equals("StockCount.Api", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(System.IO.Path.GetFileName(Path), "StockCount.Api.exe", StringComparison.OrdinalIgnoreCase);
    public string Display => $"端口 {Port} · PID {ProcessId} · {Name} · {Path ?? "路径不可读取"}";
}

public static class PortInspection
{
    private const int OwnerPidListener = 3;
    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order, int addressFamily, int tableClass, uint reserved);

    public static IReadOnlyList<PortOwner> Read(int businessPort)
    {
        var pairs = ReadFamily(2, 24, 8, 20).Concat(ReadFamily(23, 56, 20, 52))
            .Where(x => x.Port == businessPort || x.Port == businessPort + 1)
            .Distinct().OrderBy(x => x.Port).ThenBy(x => x.Pid);
        var result = new List<PortOwner>();
        foreach (var (port, pid) in pairs)
        {
            string name = "未知进程";
            string? path = null;
            long? started = null;
            try
            {
                using var process = Process.GetProcessById(pid);
                name = process.ProcessName;
                started = process.StartTime.ToUniversalTime().Ticks;
                path = process.MainModule?.FileName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException) { }
            result.Add(new(port, pid, name, path, started));
        }
        return result;
    }

    private static List<(int Port, int Pid)> ReadFamily(int family, int rowSize, int portOffset, int pidOffset)
    {
        int size = 0;
        var code = GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, OwnerPidListener, 0);
        if (code != 122 && code != 0) throw new Win32Exception((int)code);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (size < 4 || size > 32 * 1024 * 1024) throw new InvalidOperationException("TCP 监听表大小异常。");
            var allocated = size;
            var buffer = Marshal.AllocHGlobal(allocated);
            try
            {
                code = GetExtendedTcpTable(buffer, ref size, false, family, OwnerPidListener, 0);
                if (code == 122) continue;
                if (code != 0) throw new Win32Exception((int)code);
                var count = Marshal.ReadInt32(buffer);
                if (count < 0 || count > (allocated - 4) / rowSize) throw new InvalidOperationException("TCP 监听表结构异常。");
                var rows = new List<(int Port, int Pid)>(count);
                for (int i = 0; i < count; i++)
                {
                    var row = IntPtr.Add(buffer, 4 + i * rowSize);
                    var port = (ushort)IPAddress.NetworkToHostOrder(Marshal.ReadInt16(row, portOffset));
                    rows.Add((port, Marshal.ReadInt32(row, pidOffset)));
                }
                return rows;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new InvalidOperationException("TCP 监听表持续变化，请重试。");
    }

    public static PortOwner? ConfirmedExternalApi(IReadOnlyList<PortOwner> rows, int port, bool healthy)
    {
        if (!healthy || rows.Count != 2) return null;
        var first = rows[0];
        return first.Port == port && rows[1].Port == port + 1 &&
            first.IsStockCount && rows[1].IsStockCount &&
            first.ProcessId == rows[1].ProcessId &&
            first.StartedUtcTicks is not null && first.StartedUtcTicks == rows[1].StartedUtcTicks &&
            string.Equals(first.Path, rows[1].Path, StringComparison.OrdinalIgnoreCase) ? first : null;
    }
}
