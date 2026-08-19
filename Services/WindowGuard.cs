using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CIPeekShield.Models;

namespace CIPeekShield.Services;

public static class WindowGuard
{
    public static void MinimizeProcesses(IEnumerable<ProtectedEntry> entries)
    {
        var set = new HashSet<string>(entries.Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.Name)).Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
        if (set.Count == 0) return;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return true;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                if (!string.IsNullOrEmpty(p.ProcessName) && set.Contains(p.ProcessName))
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
            }
            catch { }
            return true;
        }, IntPtr.Zero);
    }

    public static void RestoreProcesses(IEnumerable<ProtectedEntry> entries)
    {
        var set = new HashSet<string>(entries.Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.Name)).Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
        if (set.Count == 0) return;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return true;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                if (!string.IsNullOrEmpty(p.ProcessName) && set.Contains(p.ProcessName))
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            }
            catch { }
            return true;
        }, IntPtr.Zero);
    }
}
