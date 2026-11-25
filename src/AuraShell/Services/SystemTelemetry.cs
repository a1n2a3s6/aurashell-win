using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace AuraShell.Services
{
    public sealed class TelemetrySnapshot
    {
        public int CpuPercent { get; init; }
        public int RamPercent { get; init; }
        public double RamUsedGb { get; init; }
        public double RamTotalGb { get; init; }
        public int DiskPercent { get; init; }
        public double DiskUsedGb { get; init; }
        public double DiskTotalGb { get; init; }
        public int BatteryPercent { get; init; }
        public bool IsCharging { get; init; }
        public bool HasBattery { get; init; }
        public double NetDownKbps { get; init; }
        public double NetUpKbps { get; init; }
        public string LocalIp { get; init; } = "127.0.0.1";
    }

    /// <summary>
    /// Low-overhead native system telemetry using Win32 GetSystemTimes, GlobalMemoryStatusEx,
    /// DriveInfo, PowerStatus, and NetworkInterface byte counters (zero PerformanceCounter lag).
    /// </summary>
    public sealed class SystemTelemetry
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
            public ulong ToUInt64() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private ulong _prevIdle;
        private ulong _prevKernel;
        private ulong _prevUser;
        private bool _cpuPrimed;

        private long _prevBytesRx;
        private long _prevBytesTx;
        private DateTime _prevNetSample = DateTime.MinValue;

        public SystemTelemetry()
        {
            SampleCpu();
            SampleNetwork();
        }

        public TelemetrySnapshot Sample()
        {
            int cpu = SampleCpu();
            var (ramPct, usedGb, totalGb) = SampleMemory();
            var (diskPct, diskUsedGb, diskTotalGb) = SampleSystemDisk();
            var (batPct, charging, hasBat) = SampleBattery();
            var (downKbps, upKbps) = SampleNetwork();
            string ip = GetLocalIpAddress();

            return new TelemetrySnapshot
            {
                CpuPercent = cpu,
                RamPercent = ramPct,
                RamUsedGb = usedGb,
                RamTotalGb = totalGb,
                DiskPercent = diskPct,
                DiskUsedGb = diskUsedGb,
                DiskTotalGb = diskTotalGb,
                BatteryPercent = batPct,
                IsCharging = charging,
                HasBattery = hasBat,
                NetDownKbps = downKbps,
                NetUpKbps = upKbps,
                LocalIp = ip
            };
        }

        private int SampleCpu()
        {
            if (!GetSystemTimes(out FILETIME idleFt, out FILETIME kernelFt, out FILETIME userFt))
                return 0;

            ulong idle = idleFt.ToUInt64();
            ulong kernel = kernelFt.ToUInt64();
            ulong user = userFt.ToUInt64();

            if (!_cpuPrimed)
            {
                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
                _cpuPrimed = true;
                return 0;
            }

            ulong idleDelta = idle - _prevIdle;
            ulong kernelDelta = kernel - _prevKernel;
            ulong userDelta = user - _prevUser;

            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;

            ulong total = kernelDelta + userDelta;
            if (total == 0) return 0;

            double busy = (double)(total - idleDelta) / total * 100.0;
            return (int)Math.Clamp(Math.Round(busy), 0, 100);
        }

        private static (int Percent, double UsedGb, double TotalGb) SampleMemory()
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref mem))
                return (0, 0, 0);

            double totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            double usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
            return ((int)mem.dwMemoryLoad, Math.Round(usedGb, 1), Math.Round(totalGb, 1));
        }

        private static (int Percent, double UsedGb, double TotalGb) SampleSystemDisk()
        {
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var drive = new DriveInfo(root);
                if (!drive.IsReady || drive.TotalSize <= 0)
                    return (0, 0, 0);

                double totalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                double usedGb = (drive.TotalSize - drive.AvailableFreeSpace) / (1024.0 * 1024.0 * 1024.0);
                int pct = (int)Math.Clamp(Math.Round(usedGb / totalGb * 100.0), 0, 100);
                return (pct, Math.Round(usedGb, 0), Math.Round(totalGb, 0));
            }
            catch
            {
                return (0, 0, 0);
            }
        }

        private static (int Percent, bool IsCharging, bool HasBattery) SampleBattery()
        {
            try
            {
                var status = WinForms.SystemInformation.PowerStatus;
                bool hasBattery = (status.BatteryChargeStatus & WinForms.BatteryChargeStatus.NoSystemBattery) == 0;
                int pct = hasBattery ? (int)Math.Clamp(Math.Round(status.BatteryLifePercent * 100f), 0, 100) : 100;
                bool charging = status.PowerLineStatus == WinForms.PowerLineStatus.Online;
                return (pct, charging, hasBattery);
            }
            catch
            {
                return (100, true, false);
            }
        }

        private (double DownKbps, double UpKbps) SampleNetwork()
        {
            long totalRx = 0;
            long totalTx = 0;

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    var stats = ni.GetIPv4Statistics();
                    totalRx += stats.BytesReceived;
                    totalTx += stats.BytesSent;
                }
            }
            catch
            {
                return (0, 0);
            }

            DateTime now = DateTime.UtcNow;
            if (_prevNetSample == DateTime.MinValue)
            {
                _prevBytesRx = totalRx;
                _prevBytesTx = totalTx;
                _prevNetSample = now;
                return (0, 0);
            }

            double elapsed = (now - _prevNetSample).TotalSeconds;
            if (elapsed < 0.2) return (0, 0);

            double downKbps = Math.Max(0, (totalRx - _prevBytesRx) / 1024.0 / elapsed);
            double upKbps = Math.Max(0, (totalTx - _prevBytesTx) / 1024.0 / elapsed);

            _prevBytesRx = totalRx;
            _prevBytesTx = totalTx;
            _prevNetSample = now;

            return (Math.Round(downKbps, 1), Math.Round(upKbps, 1));
        }

        public static string GetLocalIpAddress()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    var props = ni.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;

                    var ipv4 = props.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address));
                    if (ipv4 != null)
                        return ipv4.Address.ToString();
                }

                var fallback = Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
                return fallback?.ToString() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }
    }
}
