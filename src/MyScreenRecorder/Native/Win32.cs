using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace MyScreenRecorder.Native;

internal static class Win32
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TRANSPARENT = 0x00000020;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_LAYERED = 0x00080000;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    public static void AddExStyle(IntPtr hwnd, long style)
    {
        long current = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(current | style));
    }

    public static void MakeClickThrough(IntPtr hwnd)
    {
        AddExStyle(hwnd, WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    public static void PlaceTopmost(IntPtr hwnd, Rectangle bounds, bool show = true)
    {
        SetWindowPos(hwnd, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE | (show ? SWP_SHOWWINDOW : 0));
    }

    public static void ExcludeFromCapture(IntPtr hwnd)
    {
        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    private const uint LWA_ALPHA = 0x2;

    public static void SetOpaqueLayered(IntPtr hwnd)
    {
        SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
    }
}

internal static class KillOnCloseJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    private static readonly object Gate = new();
    private static IntPtr _job;

    private static IntPtr EnsureJob()
    {
        if (_job != IntPtr.Zero) return _job;
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        _job = job;
        return _job;
    }

    public static bool Assign(Process process)
    {
        lock (Gate)
        {
            IntPtr job = EnsureJob();
            return job != IntPtr.Zero && AssignProcessToJobObject(job, process.Handle);
        }
    }
}
