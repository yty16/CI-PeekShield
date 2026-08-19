using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CIPeekShield.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private IntPtr _hhk = IntPtr.Zero;
    private readonly Keys _key;
    private readonly Keys _modifiers;
    private readonly Action _handler;
    private readonly System.Collections.Generic.Dictionary<Keys, bool> _down = new();

    public HotkeyService(Keys modifiers, Keys key, Action handler)
    {
        _key = key;
        _modifiers = modifiers;
        _handler = handler;
        _callback = HookCallback;
        using var cur = Process.GetCurrentProcess();
        using var mod = cur.MainModule;
        _hhk = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _callback,
            NativeMethods.GetModuleHandle(null), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            var k = (Keys)vk;
            bool down = (int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN;
            bool up = (int)wParam == WM_KEYUP || (int)wParam == WM_SYSKEYUP;

            if (down)
            {
                if (k == Keys.ControlKey || k == Keys.LControlKey || k == Keys.RControlKey) _down[Keys.Control] = true;
                else if (k == Keys.ShiftKey || k == Keys.LShiftKey || k == Keys.RShiftKey) _down[Keys.Shift] = true;
                else if (k == Keys.Menu || k == Keys.LMenu || k == Keys.RMenu) _down[Keys.Alt] = true;
                else if (k == _key && MatchMods()) { try { _handler(); } catch { } }
            }
            else if (up)
            {
                if (k == Keys.ControlKey || k == Keys.LControlKey || k == Keys.RControlKey) _down[Keys.Control] = false;
                else if (k == Keys.ShiftKey || k == Keys.LShiftKey || k == Keys.RShiftKey) _down[Keys.Shift] = false;
                else if (k == Keys.Menu || k == Keys.LMenu || k == Keys.RMenu) _down[Keys.Alt] = false;
            }
        }
        return NativeMethods.CallNextHookEx(_hhk, nCode, wParam, lParam);
    }

    private bool MatchMods()
    {
        bool ctrl = _down.GetValueOrDefault(Keys.Control);
        bool shift = _down.GetValueOrDefault(Keys.Shift);
        bool alt = _down.GetValueOrDefault(Keys.Alt);
        bool wantCtrl = (_modifiers & Keys.Control) != 0;
        bool wantShift = (_modifiers & Keys.Shift) != 0;
        bool wantAlt = (_modifiers & Keys.Alt) != 0;
        return ctrl == wantCtrl && shift == wantShift && alt == wantAlt;
    }

    public void Dispose()
    {
        if (_hhk != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hhk);
            _hhk = IntPtr.Zero;
        }
    }
}
