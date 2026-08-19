using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CIPeekShield.Models;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;

namespace CIPeekShield.Services;

public class TrayService
{
    private NotifyIcon? _notifyIcon;
    private Icon? _icon;
    private ToolStripMenuItem? _pauseItem;
    private ToolStripMenuItem? _manualItem;

    public event Action? OnTogglePause;
    public event Action? OnToggleManual;
    public event Action? OnOpenSettings;
    public event Action? OnHideTray;

    public void Start()
    {
        if (_notifyIcon != null) return;
        _icon = LoadIcon();
        _notifyIcon = new NotifyIcon
        {
            Text = "CI-PeekShield · 就绪",
            Icon = _icon,
            Visible = true
        };
        _notifyIcon.Click += (_, _) => OnOpenSettings?.Invoke();

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("打开设置");
        open.Click += (_, _) => OnOpenSettings?.Invoke();
        _pauseItem = new ToolStripMenuItem("暂停防护");
        _pauseItem.Click += (_, _) => OnTogglePause?.Invoke();
        _manualItem = new ToolStripMenuItem("手动防窥：关");
        _manualItem.Click += (_, _) => OnToggleManual?.Invoke();
        var hide = new ToolStripMenuItem("隐藏托盘图标（后台静默）");
        hide.Click += (_, _) => OnHideTray?.Invoke();
        menu.Items.Add(open);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_manualItem);
        menu.Items.Add(hide);
        _notifyIcon.ContextMenuStrip = menu;
    }

    public void Show() { if (_notifyIcon != null) _notifyIcon.Visible = true; }
    public void Hide() { if (_notifyIcon != null) _notifyIcon.Visible = false; }

    public void SetTooltip(string text)
    {
        if (_notifyIcon != null) _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void SetPauseLabel(bool paused) { if (_pauseItem != null) _pauseItem.Text = paused ? "恢复防护" : "暂停防护"; }
    public void SetManualLabel(bool on) { if (_manualItem != null) _manualItem.Text = on ? "手动防窥：开" : "手动防窥：关"; }

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, int timeout = 1500)
    {
        if (_notifyIcon != null) _notifyIcon.ShowBalloonTip(timeout, title, message, icon);
    }

    public void Stop()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        if (_icon != null)
        {
            DestroyIcon(_icon.Handle);
            _icon.Dispose();
            _icon = null;
        }
    }

    public static void OpenSettingsPage()
    {
        try
        {
            var nav = IAppHost.GetService<IUriNavigationService>();
            nav.NavigateWrapped(new Uri($"classisland://app/settings/{PluginConstants.SettingsPageGuid}"), out _);
        }
        catch {  }
    }

    private static Icon LoadIcon()
    {
        try
        {
            var dir = PeekShieldSettings.PluginDir;
            var png = Path.Combine(dir, "icon.png");
            if (File.Exists(png))
            {
                using var bmp = new Bitmap(png);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
        catch { }
        using var fallback = new Bitmap(16, 16);
        using var g = Graphics.FromImage(fallback);
        g.Clear(System.Drawing.Color.SteelBlue);
        return Icon.FromHandle(fallback.GetHicon());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
