using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CIPeekShield.Models;
using ClassIsland.Core.Abstractions.Controls;

namespace CIPeekShield.Views;

public class PeekShieldNotificationSettingsControl : NotificationProviderControlBase<PeekShieldNotificationSettings>
{
    private const string _buildToken = "eXR5MTY=";
    private NumericUpDown? _durationBox;
    private NumericUpDown? _limitBox;
    private NumericUpDown? _cooldownBox;
    private readonly Dictionary<string, CheckBox> _checks = new();

    public PeekShieldNotificationSettingsControl()
    {
        _ = _buildToken;
        Build();
    }

    private void Build()
    {
        var root = new StackPanel { Spacing = 14, Margin = new Thickness(16) };

        root.Children.Add(new TextBlock
        {
            Text = "通知效果设置",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold
        });

        var durationRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var durationIcon = new TextBlock
        {
            Text = "\uec92",
            FontFamily = new FontFamily("avares://ClassIsland.Core/Assets/Fonts/FluentSystemIcons-Resizable.ttf#FluentSystemIcons-Resizable"),
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush.Parse("#AAAAAA")
        };
        var durationLabelPanel = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        durationLabelPanel.Children.Add(new TextBlock { Text = "显示时长", FontSize = 14 });
        durationLabelPanel.Children.Add(new TextBlock { Text = "通知显示的持续秒数（3-120）。", FontSize = 11, Foreground = Brush.Parse("#999999") });

        _durationBox = new NumericUpDown
        {
            Width = 100,
            Minimum = 3,
            Maximum = 120,
            Increment = 1,
            FormatString = "0",
            VerticalAlignment = VerticalAlignment.Center
        };

        durationRow.Children.Add(durationIcon);
        durationRow.Children.Add(durationLabelPanel);
        durationRow.Children.Add(_durationBox);
        root.Children.Add(durationRow);

        root.Children.Add(MakeCheckRow("\uec24", "启用强调效果", "使用当前 ClassIsland 主题色作为涟漪动画颜色。", nameof(PeekShieldNotificationSettings.EnableEffect)));
        root.Children.Add(MakeCheckRow("\ue75a", "启用语音播报", "触发警报时播报告警语音。", nameof(PeekShieldNotificationSettings.EnableSpeech)));
        root.Children.Add(MakeCheckRow("\ue74f", "启用提示音效", "触发警报时播放提示音。", nameof(PeekShieldNotificationSettings.EnableSound)));

        root.Children.Add(new Border
        {
            BorderBrush = Brush.Parse("#EEEEEE"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 6, 0, 2)
        });

        root.Children.Add(new TextBlock
        {
            Text = "同一人重复提醒冷却",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 6, 0, 4)
        });

        var limitRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        limitRow.Children.Add(new TextBlock
        {
            Text = "\uf6d8",
            FontFamily = new FontFamily("avares://ClassIsland.Core/Assets/Fonts/FluentSystemIcons-Resizable.ttf#FluentSystemIcons-Resizable"),
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush.Parse("#AAAAAA")
        });
        var limitLabel = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        limitLabel.Children.Add(new TextBlock { Text = "同一陌生人最多提醒", FontSize = 14 });
        limitLabel.Children.Add(new TextBlock { Text = "达到次数后，在冷却时间内不再对该人弹出提醒。", FontSize = 11, Foreground = Brush.Parse("#999999") });
        _limitBox = new NumericUpDown
        {
            Width = 100,
            Minimum = 1,
            Maximum = 20,
            Increment = 1,
            FormatString = "0",
            VerticalAlignment = VerticalAlignment.Center
        };
        limitRow.Children.Add(limitLabel);
        limitRow.Children.Add(_limitBox);
        root.Children.Add(limitRow);

        var coolRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        coolRow.Children.Add(new TextBlock
        {
            Text = "\ue8a6",
            FontFamily = new FontFamily("avares://ClassIsland.Core/Assets/Fonts/FluentSystemIcons-Resizable.ttf#FluentSystemIcons-Resizable"),
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush.Parse("#AAAAAA")
        });
        var coolLabel = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        coolLabel.Children.Add(new TextBlock { Text = "冷却时间（分钟）", FontSize = 14 });
        coolLabel.Children.Add(new TextBlock { Text = "达到提醒上限后，在该时长内对该人静默。", FontSize = 11, Foreground = Brush.Parse("#999999") });
        _cooldownBox = new NumericUpDown
        {
            Width = 100,
            Minimum = 1,
            Maximum = 1440,
            Increment = 5,
            FormatString = "0",
            VerticalAlignment = VerticalAlignment.Center
        };
        coolRow.Children.Add(coolLabel);
        coolRow.Children.Add(_cooldownBox);
        root.Children.Add(coolRow);

        Content = root;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Settings == null) return;

        if (_durationBox != null)
        {
            _durationBox.Value = Settings.DurationSeconds;
            _durationBox.GetObservable(NumericUpDown.ValueProperty).Subscribe(v =>
            {
                if (v.HasValue) Settings.DurationSeconds = (int)v.Value;
            });
        }

        if (_limitBox != null)
        {
            _limitBox.Value = Settings.StrangerAlertLimit;
            _limitBox.GetObservable(NumericUpDown.ValueProperty).Subscribe(v =>
            {
                if (v.HasValue) Settings.StrangerAlertLimit = (int)v.Value;
            });
        }

        if (_cooldownBox != null)
        {
            _cooldownBox.Value = Settings.StrangerAlertCooldownMinutes;
            _cooldownBox.GetObservable(NumericUpDown.ValueProperty).Subscribe(v =>
            {
                if (v.HasValue) Settings.StrangerAlertCooldownMinutes = (int)v.Value;
            });
        }

        foreach (var kv in _checks)
        {
            var prop = typeof(PeekShieldNotificationSettings).GetProperty(kv.Key, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) continue;
            var check = kv.Value;
            check.IsChecked = (bool?)prop.GetValue(Settings);
            check.GetObservable(CheckBox.IsCheckedProperty).Subscribe(v =>
            {
                if (v.HasValue) prop.SetValue(Settings, v.Value);
            });
        }
    }

    private Panel MakeCheckRow(string iconGlyph, string title, string subtitle, string settingProperty)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto") };
        var icon = new TextBlock
        {
            Text = iconGlyph,
            FontFamily = new FontFamily("avares://ClassIsland.Core/Assets/Fonts/FluentSystemIcons-Resizable.ttf#FluentSystemIcons-Resizable"),
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush.Parse("#AAAAAA"),
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(icon, 0);

        var labelPanel = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        labelPanel.Children.Add(new TextBlock { Text = title, FontSize = 14 });
        labelPanel.Children.Add(new TextBlock { Text = subtitle, FontSize = 11, Foreground = Brush.Parse("#999999") });
        Grid.SetColumn(labelPanel, 1);

        var check = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(check, 2);
        _checks[settingProperty] = check;

        row.Children.Add(icon);
        row.Children.Add(labelPanel);
        row.Children.Add(check);
        return row;
    }
}
