using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace HWWidget;

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX s);

    [DllImport("kernel32.dll")]
    static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    public static (double used, double total) Memory()
    {
        var s = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref s)) return (0, 0);
        return (s.ullTotalPhys - s.ullAvailPhys, s.ullTotalPhys);
    }

    public static bool CpuTimes(out long idle, out long kernel, out long user)
        => GetSystemTimes(out idle, out kernel, out user);

    // ---- DWM / Win32 window attributes ----
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int l, r, t, b; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hwnd, int index, int val);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_LAYERED = 0x80000;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT p);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;

    // ---- legacy accent blur (the blur-behind used by DeskBox on Win10/legacy paths):
    // it keeps blurring what is behind the window even when the window is not active,
    // which the DWM SYSTEMBACKDROP_TYPE does not do for unfocused windows.
    [StructLayout(LayoutKind.Sequential)]
    public struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;   // 0xAABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    public const int WCA_ACCENT_POLICY = 19;
    public const int ACCENT_DISABLED = 0;
    public const int ACCENT_ENABLE_BLURBEHIND = 3;
    public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    public const int DWMSBT_NONE = 1;

    public static uint Abgr(byte a, byte r, byte g, byte b)
        => (uint)((a << 24) | (b << 16) | (g << 8) | r);

    public static bool ApplyAccent(IntPtr hwnd, int state, uint gradientAbgr, int flags = 2)
    {
        var accent = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = state == ACCENT_DISABLED ? 0 : flags,
            GradientColor = state == ACCENT_DISABLED ? 0u : gradientAbgr,
            AnimationId = 0,
        };
        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size,
            };
            return SetWindowCompositionAttribute(hwnd, ref data);
        }
        catch { return false; }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    // ---- PDH, english counter paths (locale-independent) ----
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhOpenQuery(string? source, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    public static extern uint PdhCollectQueryData(IntPtr query);

    /// <summary>PDH_FMT_COUNTERVALUE: DWORD status + union (8 bytes). Declaring it as a
    /// plain 'out double' makes PDH write 16 bytes into an 8-byte buffer (access violation).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE
    {
        public uint CStatus;
        public double doubleValue;
    }

    [DllImport("pdh.dll")]
    public static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type,
                                                          out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll")]
    public static extern uint PdhCloseQuery(IntPtr query);

    public const uint PDH_FMT_DOUBLE = 0x00000200;

    public const int GWL_STYLE = -16;
    public const int WS_THICKFRAME = 0x40000;
    public const int WS_MAXIMIZEBOX = 0x10000;
    public const int WS_MINIMIZEBOX = 0x20000;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOSIZE = 0x0001;
}

/// <summary>Throughput of the busiest active adapter, from GetIfTable2 (one syscall, 64-bit
/// counters covering IPv4+IPv6, link speed included — no counters, no WMI, no admin).</summary>
internal sealed class NetSampler
{
    // CharSet.Unicode is load-bearing: WCHAR Alias[257]/Description[257] must marshal as 2 bytes/char,
    // otherwise every offset after them shifts and the counters read as garbage.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MIB_IF_ROW2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Alias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Description;
        public uint PhysicalAddressLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PhysicalAddress;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PermanentPhysicalAddress;
        public uint Mtu, Type, TunnelType, MediaType, PhysicalMediumType, AccessType, DirectionType;
        public byte InterfaceAndOperStatusFlags;   // 8x BOOLEAN:1 -> a single byte in C
        public uint OperStatus, AdminStatus, MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed, ReceiveLinkSpeed;
        public ulong InOctets, InUcastPkts, InNUcastPkts, InDiscards, InErrors, InUnknownProtos,
                     InUcastOctets, InMulticastOctets, InBroadcastOctets;
        public ulong OutOctets, OutUcastPkts, OutNUcastPkts, OutDiscards, OutErrors,
                     OutUcastOctets, OutMulticastOctets, OutBroadcastOctets, OutQLen;
    }

    [DllImport("iphlpapi.dll")]
    static extern uint GetIfTable2(out IntPtr table);
    [DllImport("iphlpapi.dll")]
    static extern void FreeMibTable(IntPtr table);

    internal static int RowSize => Marshal.SizeOf<MIB_IF_ROW2>();

    // read the fields we need by fixed offset: marshalling the whole 1352-byte row
    // (two 257-WCHAR strings, 50 rows) every second was the widget's biggest CPU cost
    static readonly int OffLuid = Marshal.OffsetOf<MIB_IF_ROW2>(nameof(MIB_IF_ROW2.InterfaceLuid)).ToInt32();
    static readonly int OffAlias = Marshal.OffsetOf<MIB_IF_ROW2>(nameof(MIB_IF_ROW2.Alias)).ToInt32();
    static readonly int OffDesc = Marshal.OffsetOf<MIB_IF_ROW2>(nameof(MIB_IF_ROW2.Description)).ToInt32();
    static readonly int OffType = Marshal.OffsetOf<MIB_IF_ROW2>(nameof(MIB_IF_ROW2.Type)).ToInt32();
    static readonly int OffTunnel = Marshal.OffsetOf<MIB_IF_ROW2>(nameof(MIB_IF_ROW2.TunnelType)).ToInt32();
    static readonly int OffOper = Marshal.OffsetOf<MIB_IF_ROW2>(nameof(MIB_IF_ROW2.OperStatus)).ToInt32();
    static readonly int OffMedia = Marshal.OffsetOf<MIB_IF_ROW2>(nameof(MIB_IF_ROW2.MediaConnectState)).ToInt32();
    static readonly int OffLink = Marshal.OffsetOf<MIB_IF_ROW2>(nameof(MIB_IF_ROW2.ReceiveLinkSpeed)).ToInt32();
    static readonly int OffIn = Marshal.OffsetOf<MIB_IF_ROW2>(nameof(MIB_IF_ROW2.InOctets)).ToInt32();
    static readonly int OffOut = Marshal.OffsetOf<MIB_IF_ROW2>(nameof(MIB_IF_ROW2.OutOctets)).ToInt32();

    sealed class Rate
    {
        public ulong Luid; public string Alias = ""; public string Desc = "";
        public double Down, Up, Link;
        public double Total => Down + Up;
    }

    readonly Dictionary<ulong, (ulong rx, ulong tx)> _prev = new();
    long _t0 = Stopwatch.GetTimestamp();
    ulong _chosen;

    public double DownBps { get; private set; }
    public double UpBps { get; private set; }
    public double LinkBps { get; private set; }
    public string AdapterName { get; private set; } = "n/d";

    public void RefreshAdapters() { }   // adapters are re-read every sample

    /// <summary>Raw per-interface counters: (luid, alias, description, rxBytes, txBytes, linkBps).</summary>
    internal static List<(ulong luid, string alias, string desc, ulong rx, ulong tx, ulong link)> Read()
    {
        var list = new List<(ulong, string, string, ulong, ulong, ulong)>();
        if (GetIfTable2(out IntPtr table) != 0) return list;
        try
        {
            uint count = (uint)Marshal.ReadInt32(table);
            int rowSize = RowSize;
            for (uint i = 0; i < count; i++)
            {
                IntPtr row = table + 8 + (int)i * rowSize;
                if (Marshal.ReadInt32(row + OffOper) != 1) continue;   // IfOperStatusUp
                if (Marshal.ReadInt32(row + OffMedia) != 1) continue;  // MediaConnectStateConnected
                if (Marshal.ReadInt32(row + OffType) == 24) continue;  // loopback
                if (Marshal.ReadInt32(row + OffTunnel) != 0) continue; // tunnel
                string alias = Marshal.PtrToStringUni(row + OffAlias) ?? "";
                string desc = Marshal.PtrToStringUni(row + OffDesc) ?? "";
                if (alias.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsFilterDriver(alias) || IsFilterDriver(desc)) continue;
                list.Add(((ulong)Marshal.ReadInt64(row + OffLuid), alias, desc,
                          (ulong)Marshal.ReadInt64(row + OffIn), (ulong)Marshal.ReadInt64(row + OffOut),
                          (ulong)Marshal.ReadInt64(row + OffLink)));
            }
        }
        finally { FreeMibTable(table); }
        return list;
    }

    /// <summary>NDIS lightweight filters show up as interfaces too; they'd win the "busiest" pick
    /// whenever the real NIC is idle.</summary>
    static bool IsFilterDriver(string s) =>
        s.Contains("WFP", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("LightWeight Filter", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("QoS Packet Scheduler", StringComparison.OrdinalIgnoreCase);

    public void Sample()
    {
        var rows = Read();

        long now = Stopwatch.GetTimestamp();
        double dt = (now - _t0) / (double)Stopwatch.Frequency;
        if (dt < 0.15) return;
        _t0 = now;

        var rates = new List<Rate>(rows.Count);
        foreach (var r in rows)
        {
            double down = 0, up = 0;
            if (_prev.TryGetValue(r.luid, out var p))
            {
                down = Math.Max(0, (double)(r.rx - p.rx)) / dt;
                up = Math.Max(0, (double)(r.tx - p.tx)) / dt;
            }
            _prev[r.luid] = (r.rx, r.tx);
            rates.Add(new Rate { Luid = r.luid, Alias = r.alias, Desc = r.desc, Down = down, Up = up, Link = r.link });
        }
        if (rates.Count == 0) { DownBps = UpBps = LinkBps = 0; AdapterName = "n/d"; return; }

        var best = rates.MaxBy(r => r.Total)!;
        if (best.Total == 0) best = rates.MaxBy(r => r.Link)!;   // idle: prefer the fastest link

        // sticky: don't hop between adapters for trickle traffic
        if (_chosen != 0 && best.Luid != _chosen && best.Total < 256 * 1024)
            best = rates.Find(r => r.Luid == _chosen) ?? best;

        _chosen = best.Luid;
        DownBps = best.Down;
        UpBps = best.Up;
        LinkBps = best.Link;
        AdapterName = best.Desc.Length > 0 ? best.Desc : best.Alias;
    }
}

/// <summary>CPU load via GetSystemTimes (locale-independent, no counters, no admin).</summary>
internal sealed class CpuSampler
{
    long _idle, _kernel, _user;
    bool _primed;

    public double Usage { get; private set; }
    public string Name { get; private set; } = "CPU";
    public uint BaseMhz { get; private set; }
    public uint CurrentMhz { get; private set; }
    public string? FreqNote { get; private set; }

    public void Prime() { _primed = Native.CpuTimes(out _idle, out _kernel, out _user); }

    public void Sample()
    {
        if (!_primed) { Prime(); return; }
        if (!Native.CpuTimes(out long idle, out long kernel, out long user)) return;

        long dIdle = idle - _idle, dKernel = kernel - _kernel, dUser = user - _user;
        _idle = idle; _kernel = kernel; _user = user;

        long total = dKernel + dUser;                    // kernel time includes idle time
        if (total <= 0) return;
        Usage = Math.Clamp((total - dIdle) * 100.0 / total, 0, 100);
    }

    /// <summary>Static info + clock. Win32_Processor.CurrentClockSpeed is live on most Intel parts,
    /// static on AMD (no user-mode source exists there) — see FreqNote.</summary>
    public void LoadStatic()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed, CurrentClockSpeed FROM Win32_Processor");
            foreach (ManagementObject mo in searcher.Get())
            {
                Name = (mo["Name"] as string)?.Trim() ?? "CPU";
                BaseMhz = Convert.ToUInt32(mo["MaxClockSpeed"] ?? 0u);
                CurrentMhz = Convert.ToUInt32(mo["CurrentClockSpeed"] ?? 0u);
                if (CurrentMhz == 0) CurrentMhz = BaseMhz;
                break;
            }
        }
        catch { }
        if (BaseMhz == 0) { BaseMhz = 1000; CurrentMhz = 1000; }
        FreqNote = CurrentMhz >= BaseMhz ? "base" : null;
    }

    public void RefreshClock()
    {
        if (BaseMhz == 0) return;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
            foreach (ManagementObject mo in searcher.Get())
            {
                uint mhz = Convert.ToUInt32(mo["CurrentClockSpeed"] ?? 0u);
                if (mhz > 0) { CurrentMhz = mhz; FreqNote = mhz >= BaseMhz ? "base" : null; }
                break;
            }
        }
        catch { }
    }
}

/// <summary>Disk throughput from PDH with english counter paths (no admin, no WMI):
/// PDH computes the bytes/sec rate itself.</summary>
internal sealed class DiskSampler
{
    IntPtr _query, _read, _write;
    bool _opened;

    public bool Available { get; private set; }
    public double ReadBps { get; private set; }
    public double WriteBps { get; private set; }
    public string Name { get; private set; } = "_Total";

    public void Open()
    {
        if (_opened) return;
        _opened = true;
        try
        {
            if (Native.PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) return;
            uint r = Native.PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", IntPtr.Zero, out _read);
            uint w = Native.PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", IntPtr.Zero, out _write);
            if (r != 0 || w != 0) return;
            Native.PdhCollectQueryData(_query);
            Available = true;
        }
        catch { Available = false; }
    }

    public void Sample()
    {
        if (!Available) return;
        try
        {
            if (Native.PdhCollectQueryData(_query) != 0) return;
            ReadBps = Read(_read);
            WriteBps = Read(_write);
        }
        catch { }
    }

    static double Read(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return 0;
        if (Native.PdhGetFormattedCounterValue(counter, Native.PDH_FMT_DOUBLE, out _, out var v) != 0) return 0;
        return v.CStatus == 0 && v.doubleValue > 0 ? v.doubleValue : 0;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) { try { Native.PdhCloseQuery(_query); } catch { } _query = IntPtr.Zero; }
    }
}

internal sealed class RamSampler
{
    public double UsedGb { get; private set; }
    public double TotalGb { get; private set; }
    public double UsagePct { get; private set; }
    public string SpeedText { get; private set; } = "";

    public void Sample()
    {
        var (used, total) = Native.Memory();
        if (total <= 0) return;
        UsedGb = used / 1073741824.0;
        TotalGb = total / 1073741824.0;
        UsagePct = used * 100.0 / total;
    }

    /// <summary>Configured (EXPO/XMP) speed — static by nature, so read once.</summary>
    public void LoadStatic()
    {
        try
        {
            var speeds = new List<uint>();
            using (var searcher = new ManagementObjectSearcher("SELECT Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
                foreach (ManagementObject mo in searcher.Get())
                {
                    uint s = Convert.ToUInt32(mo["ConfiguredClockSpeed"] ?? 0u);
                    if (s == 0) s = Convert.ToUInt32(mo["Speed"] ?? 0u);
                    if (s > 0) speeds.Add(s);
                }
            if (speeds.Count > 0)
            {
                uint max = speeds.Max();
                SpeedText = speeds.All(x => x == max) ? $"{max} MT/s" : $"{speeds.Min()}–{max} MT/s";
            }
        }
        catch { }
    }
}

/// <summary>GPU metrics through NVML (ships with the NVIDIA driver: nvml.dll in System32, no admin).</summary>
internal sealed class GpuSampler : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct NvmlUtilization { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)]
    struct NvmlMemory { public ulong Total, Free, Used; }

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] static extern int Init();
    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")] static extern int Shutdown();
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2")] static extern int GetCount(out uint count);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] static extern int GetHandle(uint index, out IntPtr device);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName")] static extern int GetName(IntPtr d, byte[] name, uint len);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")] static extern int GetUtil(IntPtr d, out NvmlUtilization u);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")] static extern int GetMem(IntPtr d, out NvmlMemory m);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")] static extern int GetPower(IntPtr d, out uint mw);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetEnforcedPowerLimit")] static extern int GetPowerLimit(IntPtr d, out uint mw);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")] static extern int GetTemp(IntPtr d, int sensor, out uint c);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")] static extern int GetClock(IntPtr d, int type, out uint mhz);

    IntPtr _dev;
    bool _booted;

    public bool Available { get; private set; }
    public string Name { get; private set; } = "";
    public uint Util { get; private set; }
    public uint MemUtil { get; private set; }
    public double VramUsedGb { get; private set; }
    public double VramTotalGb { get; private set; }
    public double Watts { get; private set; }
    public double WattsLimit { get; private set; }
    public double TempC { get; private set; }
    public double ClockMhz { get; private set; }
    public int DeviceCount { get; private set; }

    public bool Open(int index = 0)
    {
        if (!_booted) { if (Init() != 0) return false; _booted = true; }
        if (GetCount(out uint n) != 0) return false;
        DeviceCount = (int)n;
        if (index >= n) return false;
        if (GetHandle((uint)index, out _dev) != 0) return false;

        var buf = new byte[96];
        if (GetName(_dev, buf, 96u) == 0)
            Name = System.Text.Encoding.ASCII.GetString(buf).TrimEnd('\0');
        if (GetPowerLimit(_dev, out uint lim) == 0) WattsLimit = lim / 1000.0;
        Available = true;
        return true;
    }

    public void Sample()
    {
        if (!Available) return;
        if (GetUtil(_dev, out var u) == 0) { Util = u.Gpu; MemUtil = u.Memory; }
        if (GetMem(_dev, out var m) == 0)
        {
            VramUsedGb = m.Used / 1073741824.0;
            VramTotalGb = m.Total / 1073741824.0;
        }
        if (GetPower(_dev, out uint mw) == 0) Watts = mw / 1000.0;
        if (GetTemp(_dev, 0, out uint t) == 0) TempC = t;
        if (GetClock(_dev, 0, out uint c) == 0) ClockMhz = c;
    }

    public void Dispose() { if (_booted) { try { Shutdown(); } catch { } _booted = false; } }
}

/// <summary>Single sampler for the whole app: N widgets share one set of reads, so
/// adding widgets doesn't multiply the cost.</summary>
internal static class SensorHub
{
    static readonly NetSampler Net = new();
    static readonly CpuSampler Cpu = new();
    static readonly RamSampler Ram = new();
    static readonly DiskSampler Disk = new();
    static readonly GpuSampler Gpu = new();
    static DispatcherTimer? _timer;
    static DispatcherTimer? _slow;
    static readonly Dictionary<string, double> Requested = new();
    static AiSnapshot _ai = new();
    static DateTime _aiLast = DateTime.MinValue;
    static DispatcherTimer? _aiTimer;
    static bool _aiBusy;

    public static Metrics Current { get; private set; } = new();
    public static event Action<Metrics>? Tick;

    public static void Start(double intervalSeconds)
    {
        Cpu.Prime();
        Cpu.LoadStatic();
        Ram.LoadStatic();
        Ram.Sample();
            Disk.Open();
        Gpu.Open(0);
        StartAiRefresh();
        Sample();
        SetInterval("__start", intervalSeconds);
    }

    /// <summary>Le API AI si aggiornano molto più lentamente del resto (default 15 min)
    /// e la chiamata HTTP resta fuori dal thread dell'interfaccia.</summary>
    static void StartAiRefresh()
    {
        if (_aiTimer != null) return;
        _aiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _aiTimer.Tick += (_, _) =>
        {
            int minutes = Math.Max(1, AiKeys.Load().RefreshMinutes);
            if ((DateTime.UtcNow - _aiLast).TotalMinutes < minutes) return;
            _ = RefreshAiAsync();
        };
        _aiTimer.Start();
        _ = RefreshAiAsync();
    }

    public static async Task RefreshAiAsync()
    {
        if (_aiBusy) return;
        _aiBusy = true;
        try
        {
            var keys = AiKeys.Load();
            _ai = await Task.Run(() => AiUsage.FetchAsync(keys));
        }
        catch { }
        finally
        {
            _aiBusy = false;
            _aiLast = DateTime.UtcNow;
            Sample();
        }
    }

    public static void SetInterval(string widgetId, double seconds)
    {
        Requested[widgetId] = Math.Clamp(seconds, 0.5, 5);
        double s = Requested.Values.Min();
        if (_timer == null)
        {
            _timer = new DispatcherTimer();
            _timer.Tick += (_, _) => Sample();
        }
        if (Math.Abs(_timer.Interval.TotalSeconds - s) > 0.01) _timer.Interval = TimeSpan.FromSeconds(s);
        _timer.Start();

        if (_slow == null)
        {
            // WMI clock read is the most expensive source and the value is static on most AMD parts
            _slow = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _slow.Tick += (_, _) => Cpu.RefreshClock();
            _slow.Start();
        }
    }

    public static void Forget(string widgetId)
    {
        Requested.Remove(widgetId);
        if (_timer != null && Requested.Count > 0)
            _timer.Interval = TimeSpan.FromSeconds(Requested.Values.Min());
    }

    static void Sample()
    {
        Net.Sample();
        Cpu.Sample();
        Ram.Sample();
        Disk.Sample();
        Gpu.Sample();

        var m = new Metrics
        {
            NetDown = Net.DownBps,
            NetUp = Net.UpBps,
            NetLink = Net.LinkBps,
            NetName = Net.AdapterName,
            CpuUsage = Cpu.Usage,
            CpuMhz = Cpu.CurrentMhz,
            CpuBaseMhz = Cpu.BaseMhz,
            CpuName = Cpu.Name,
            GpuOk = Gpu.Available,
            GpuUtil = Gpu.Util,
            VramUsed = Gpu.VramUsedGb,
            VramTotal = Gpu.VramTotalGb,
            Watts = Gpu.Watts,
            WattsLimit = Gpu.WattsLimit,
            TempC = Gpu.TempC,
            ClockMhz = Gpu.ClockMhz,
            GpuName = Gpu.Name,
            RamUsed = Ram.UsedGb,
            RamTotal = Ram.TotalGb,
            RamPct = Ram.UsagePct,
            RamSpeed = Ram.SpeedText,
            DiskOk = Disk.Available,
            DiskRead = Disk.ReadBps,
            DiskWrite = Disk.WriteBps,
            Ai = _ai,
        };
        Current = m;
        Tick?.Invoke(m);
    }
}
