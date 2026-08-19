using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CIPeekShield.Services;

public class OverlayService
{
    private ProtectWindow? _protect;
    private FogWindow? _fog;
    private PopupWindow? _popup;

    private bool _protectOn;
    private bool _fogOn;
    private bool _popupOn;

    public event Action? Dismissed;

    public bool IsProtectOn => _protectOn;
    public bool IsFogOn => _fogOn;
    public bool IsPopupOn => _popupOn;

    public void ShowProtect()
    {
        if (_protectOn) return;
        Dispatcher.UIThread.Post(() =>
        {
            var screen = GetForegroundScreen();
            _protect = new ProtectWindow(screen);
            _protect.ContinueRequested += OnProtectContinue;
            _protect.Show();
            _protectOn = true;
        });
    }

    public void ShowFog(string? message = null)
    {
        if (_fogOn) { _fog?.SetMessage(message); return; }
        Dispatcher.UIThread.Post(() =>
        {
            _fog = new FogWindow();
            _fog.SetMessage(message);
            _fog.Show();
            _fogOn = true;
        });
    }

    public void ShowPopup(string message)
    {
        if (_popupOn) { _popup?.SetMessage(message); return; }
        Dispatcher.UIThread.Post(() =>
        {
            _popup = new PopupWindow();
            _popup.SetMessage(message);
            _popup.Show();
            _popupOn = true;
        });
    }

    public void HideAll()
    {
        if (!_protectOn && !_fogOn && !_popupOn) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_protect != null)
            {
                try { _protect.ContinueRequested -= OnProtectContinue; _protect.Close(); } catch { }
                _protect = null;
            }
            if (_fog != null) { try { _fog.Close(); } catch { } _fog = null; }
            if (_popup != null) { try { _popup.Close(); } catch { } _popup = null; }
        });
        _protectOn = false;
        _fogOn = false;
        _popupOn = false;
    }

    private void OnProtectContinue()
    {
        Dismissed?.Invoke();
    }

    private static System.Windows.Forms.Screen? GetForegroundScreen()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                var s = System.Windows.Forms.Screen.FromHandle(hwnd);
                if (s != null) return s;
            }
        }
        catch { }
        return System.Windows.Forms.Screen.PrimaryScreen;
    }

    private class ProtectWindow : Window
    {
        public event Action? ContinueRequested;

        public ProtectWindow(System.Windows.Forms.Screen? screen)
        {
            SystemDecorations = SystemDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = true;
            Focusable = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x22));

            if (screen != null)
            {
                Position = new PixelPoint(screen.Bounds.X, screen.Bounds.Y);
                Width = screen.Bounds.Width;
                Height = screen.Bounds.Height;
            }
            else
            {
                WindowState = WindowState.FullScreen;
            }

            var icon = new TextBlock
            {
                Text = "\ud83d\udee1",
                FontSize = 72,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Brushes.White
            };

            var title = new TextBlock
            {
                Text = "隐私保护中",
                FontSize = 24,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 0)
            };

            var btn = new Button
            {
                Content = "继续查看",
                FontSize = 14,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x76, 0xF6)),
                Padding = new Thickness(32, 10),
                CornerRadius = new CornerRadius(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 28, 0, 0)
            };
            btn.Click += (_, _) => Continue();

            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { icon, title, btn }
            };

            Content = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x22)),
                Children = { panel }
            };

            KeyDown += OnKeyDown;
            PointerPressed += OnPointerPressed;
        }

        private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Space || e.Key == Key.Enter)
                Continue();
        }

        private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (e.Source is Button) return;
            Continue();
        }

        private void Continue()
        {
            ContinueRequested?.Invoke();
            Close();
        }
    }

    private class FogWindow : Window
    {
        private readonly TextBlock _text;
        public FogWindow()
        {
            SystemDecorations = SystemDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Blur };
            Background = Brushes.Transparent;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;

            var total = GetVirtualBounds();
            Position = new PixelPoint(total.X, total.Y);
            Width = total.Width;
            Height = total.Height;

            var grid = new Grid();
            grid.Children.Add(new Border
            {
                Background = new SolidColorBrush(Colors.Black, 0.42),
                IsHitTestVisible = false
            });
            _text = new TextBlock
            {
                Text = "检测到他人正在窥视你的屏幕",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                IsVisible = true
            };
            grid.Children.Add(new Border
            {
                Child = _text,
                Background = new SolidColorBrush(Colors.Black, 0.35),
                Padding = new Thickness(24, 12),
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            });
            Content = grid;
        }
        public void SetMessage(string? msg) => Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrEmpty(msg)) _text.Text = msg;
        });
    }

    private class PopupWindow : Window
    {
        private readonly TextBlock _text;
        public PopupWindow()
        {
            SystemDecorations = SystemDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = 460; Height = 150;
            _text = new TextBlock
            {
                Text = "检测到他人正在窥视你的屏幕",
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeight.Bold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28), 0.92),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                Child = _text
            };
        }
        public void SetMessage(string msg) => Dispatcher.UIThread.Post(() => _text.Text = msg);
    }

    private static System.Drawing.Rectangle GetVirtualBounds()
    {
        try
        {
            return System.Windows.Forms.Screen.AllScreens.Aggregate(System.Drawing.Rectangle.Empty,
                (acc, s) => System.Drawing.Rectangle.Union(acc, s.Bounds));
        }
        catch
        {
            return new System.Drawing.Rectangle(0, 0, 1920, 1080);
        }
    }
}
