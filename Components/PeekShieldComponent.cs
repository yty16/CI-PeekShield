using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CIPeekShield.Models;
using CIPeekShield.Services;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;

namespace CIPeekShield.Components;

[ComponentInfo(PluginConstants.ComponentGuid, "智能防窥",
    description: "CI-PeekShield 隐私防护状态（实时人脸数 / 偷窥告警）")]
public class PeekShieldComponent : ComponentBase
{
    private Border? _indicator;
    private TextBlock? _statusText;
    private TextBlock? _faceText;
    private readonly PeekShieldEngine _engine = PeekShieldEngine.Instance;

    public PeekShieldComponent()
    {
        this.Unloaded += OnUnloaded;
        _engine.StatusChanged += OnStatus;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Build();
        Update(_engine.Status);
    }

    private void Build()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            Margin = new Thickness(10),
            MinHeight = 90
        };

        _indicator = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
            Background = Brush.Parse("#2196F3"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = "🛡",
                FontSize = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetRow(_indicator, 0);
        grid.Children.Add(_indicator);

        _statusText = new TextBlock
        {
            Text = "监控中",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        Grid.SetRow(_statusText, 1);
        grid.Children.Add(_statusText);

        _faceText = new TextBlock
        {
            Text = "👁 人脸：0",
            FontSize = 11,
            Foreground = Brush.Parse("#888888"),
            TextAlignment = TextAlignment.Center
        };
        Grid.SetRow(_faceText, 2);
        grid.Children.Add(_faceText);

        Content = grid;
    }

    private void OnStatus(EngineStatus s) => Dispatcher.UIThread.Post(() => Update(s));

    private void Update(EngineStatus s)
    {
        if (_statusText != null) _statusText.Text = PeekShieldEngine.StatusText(s);
        if (_faceText != null)
        {

            var dist = _engine.LastMatchDistance;
            _faceText.Text = (dist >= 0 && dist < double.MaxValue)
                ? $"👁 人脸：{_engine.FaceCount} · 相似 {dist:F2}"
                : $"👁 人脸：{_engine.FaceCount}";
        }
        if (_indicator != null)
        {
            var color = s switch
            {
                EngineStatus.Peek => "#F44336",
                EngineStatus.Secure => "#4CAF50",
                EngineStatus.Monitoring => "#2196F3",
                EngineStatus.Paused => "#FF9800",
                EngineStatus.Manual => "#9C27B0",
                EngineStatus.NotEnrolled => "#FF9800",
                EngineStatus.NoCamera => "#9E9E9E",
                _ => "#9E9E9E"
            };
            _indicator.Background = Brush.Parse(color);
        }
    }

    private void OnUnloaded(object? sender, System.EventArgs e)
    {
        _engine.StatusChanged -= OnStatus;
        this.Unloaded -= OnUnloaded;
    }
}
