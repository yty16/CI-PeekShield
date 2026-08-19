using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace CIPeekShield.Services;

public class ForegroundWatcher
{
    public bool IsLocked { get; private set; }
    public string? ForegroundProcessName { get; private set; }
    public string? ForegroundWindowTitle { get; private set; }
    public event Action? StateChanged;

    public void Start()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        RefreshForeground();
    }

    public void Stop()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        IsLocked = e.Reason == SessionSwitchReason.SessionLock;
        RefreshForeground();
        StateChanged?.Invoke();
    }

    public void RefreshForeground()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) { ForegroundProcessName = null; ForegroundWindowTitle = null; return; }
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) { ForegroundProcessName = null; ForegroundWindowTitle = null; return; }
            using var p = Process.GetProcessById((int)pid);
            ForegroundProcessName = p.ProcessName;
            ForegroundWindowTitle = GetWindowTitle(hwnd);
        }
        catch
        {
            ForegroundProcessName = null;
            ForegroundWindowTitle = null;
        }
    }

    private static string? GetWindowTitle(IntPtr hwnd)
    {
        try
        {
            int len = NativeMethods.GetWindowTextLength(hwnd);
            if (len <= 0) return string.Empty;
            var sb = new System.Text.StringBuilder(len + 1);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }
}
