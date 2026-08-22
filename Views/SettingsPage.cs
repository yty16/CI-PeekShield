using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CIPeekShield.Models;
using CIPeekShield.Services;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;

namespace CIPeekShield.Views;

[SettingsPageInfo(PluginConstants.SettingsPageGuid, "CI-PeekShield 设置", "👀", "👀")]
public partial class SettingsPage : SettingsPageBase
{
    private readonly PeekShieldEngine _engine = PeekShieldEngine.Instance;
    private PeekShieldSettings S => _engine.Settings;

    private readonly ObservableCollection<ProtectedEntry> _procList = new();
    private readonly ObservableCollection<ProtectedEntry> _titleList = new();

    private class CamItem
    {
        public int Index;
        public string Name = "";
        public override string ToString() => Name;
    }

    private TextBlock? _statusText;
    private TextBlock? _enrollHint;
    private TextBlock? _camTestText;
    private ComboBox? _camComboBox;
    private ComboBox? _sensComboBox;
    private StackPanel? _procHost;
    private TextBox _procInput = null!;
    private StackPanel? _titleHost;
    private TextBox _titleInput = null!;
    private TextBox? _hkModBox;
    private TextBox? _hkKeyBox;
    private Button? _enrollBtn;
    private Button? _clearBtn;
    private Button? _photoBtn;
    private CheckBox? _enableSmartPeekCheck;
    private CheckBox? _pausedCheck;
    private bool _updatingUi;

    public SettingsPage()
    {
        this.Unloaded += OnUnloaded;
        _engine.StatusChanged += OnStatus;
        _engine.SettingsChanged += OnSettingsChanged;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Build();
        RefreshStatus();
    }

    private void Build()
    {
        var scroll = new ScrollViewer();
        var root = new StackPanel { Spacing = 14, Margin = new Thickness(16) };

        root.Children.Add(new TextBlock
        {
            Text = "CI-PeekShield 智能防窥",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold
        });

        _statusText = new TextBlock
        {
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.Children.Add(_statusText);

        root.Children.Add(BuildEnrollSection());
        root.Children.Add(BuildCameraSection());
        root.Children.Add(BuildSensitivitySection());
        root.Children.Add(BuildActionsSection());
        root.Children.Add(BuildProtectSection());
        root.Children.Add(BuildSuppressSection());
        root.Children.Add(BuildAdvancedSection());
        root.Children.Add(BuildMasterSection());

        scroll.Content = root;
        Content = scroll;
    }

    private StackPanel BuildEnrollSection()
    {
        var s = Section("人脸录入（本地存储，禁止上传）");
        _enrollHint = new TextBlock
        {
            FontSize = 11,
            Foreground = Brush.Parse("#999999"),
            TextWrapping = TextWrapping.Wrap,
            Text = _engine.IsEnrolled ? "已录入机主人脸，可重新录入或清空。" : "尚未录入，可点击「录入人脸」正对摄像头，或点「上传照片录入」选择一张正脸照片完成录入。"
        };
        s.Children.Add(_enrollHint);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        _enrollBtn = MakeButton(_engine.IsEnrolled ? "重新录入" : "录入人脸", async (_) => await DoEnroll(_engine.IsEnrolled));
        _clearBtn = MakeButton("清空人脸数据", (_) => { _engine.ClearEnrollment(); RefreshStatus(); UpdateEnrollHint(); RefreshEnrollButton(); });
        _photoBtn = MakeButton("上传照片录入", async (_) => await DoEnrollPhoto());
        row.Children.Add(_enrollBtn);
        row.Children.Add(_clearBtn);
        row.Children.Add(_photoBtn);
        s.Children.Add(row);
        return s;
    }

    private async Task DoEnroll(bool reEnroll)
    {
        if (_enrollBtn != null) _enrollBtn.IsEnabled = false;
        if (_clearBtn != null) _clearBtn.IsEnabled = false;
        UpdateEnrollHint("录入中… 请正对摄像头保持静止（约 3 秒）");
        bool ok = await _engine.EnrollAsync(12, (n) => UpdateEnrollHint($"已采集 {n} 张人脸样本…"));
        UpdateEnrollHint(ok ? "✓ 录入成功" : "✗ 录入失败：未采集到足够清晰的人脸，请重试");
        if (_enrollBtn != null) _enrollBtn.IsEnabled = true;
        if (_clearBtn != null) _clearBtn.IsEnabled = true;
        RefreshStatus();
        RefreshEnrollButton();
    }

    private async Task DoEnrollPhoto()
    {
        var dlg = new System.Windows.Forms.OpenFileDialog
        {
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*",
            Title = "选择一张包含你正脸的人脸照片"
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var path = dlg.FileName;
        if (_enrollBtn != null) _enrollBtn.IsEnabled = false;
        if (_clearBtn != null) _clearBtn.IsEnabled = false;
        if (_photoBtn != null) _photoBtn.IsEnabled = false;
        UpdateEnrollHint("照片录入中… 正在本地分析人脸特征");
        bool ok = await _engine.EnrollFromPhotoAsync(path, (n) => UpdateEnrollHint($"已生成 {n} 个人脸特征样本…"));
        UpdateEnrollHint(ok ? "✓ 照片录入成功" : "✗ 未从照片中检测到清晰正脸，请换一张重新上传");
        if (_enrollBtn != null) _enrollBtn.IsEnabled = true;
        if (_clearBtn != null) _clearBtn.IsEnabled = true;
        if (_photoBtn != null) _photoBtn.IsEnabled = true;
        RefreshStatus();
        RefreshEnrollButton();
    }

    private void UpdateEnrollHint(string? text = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_enrollHint == null) return;
            _enrollHint.Text = text ?? (_engine.IsEnrolled ? "已录入机主人脸，可重新录入或清空。" : "尚未录入，可点击「录入人脸」正对摄像头，或点「上传照片录入」选择一张正脸照片完成录入。");
        });
    }

    private void RefreshEnrollButton()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_enrollBtn == null) return;
            _enrollBtn.Content = _engine.IsEnrolled ? "重新录入" : "录入人脸";
        });
    }

    private StackPanel BuildCameraSection()
    {
        var s = Section("摄像头设备");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        _camComboBox = new ComboBox { Width = 240, Margin = new Thickness(0, 4, 0, 0) };
        var cams = CameraService.Enumerate();
        foreach (var c in cams) _camComboBox.Items.Add(new CamItem { Index = c.index, Name = c.name });
        if (_camComboBox.Items.Count == 0)
            _camComboBox.Items.Add(new CamItem { Index = 0, Name = "默认摄像头 (0)" });

        for (int i = 0; i < _camComboBox.Items.Count; i++)
            if (((CamItem)_camComboBox.Items[i]!).Index == S.CameraIndex) { _camComboBox.SelectedIndex = i; break; }
        if (_camComboBox.SelectedIndex < 0) _camComboBox.SelectedIndex = 0;
        _camComboBox.SelectionChanged += (_, _) =>
        {
            if (_camComboBox.SelectedItem is CamItem ci)
            {
                S.CameraIndex = ci.Index; S.CameraName = ci.Name; S.Save(); _engine.RestartCamera();
            }
        };
        row.Children.Add(_camComboBox);

        var testBtn = MakeButton("测试打开", (_) =>
        {
            try
            {
                using var cam = new CameraService();
                bool ok = cam.Open(S.CameraIndex);
                _camTestText!.Text = ok ? $"✓ 摄像头可用：{S.CameraIndex}" : $"✗ {cam.LastError}";
                cam.Close();
            }
            catch (Exception ex) { _camTestText!.Text = $"✗ 测试异常：{ex.Message}"; }
        });
        row.Children.Add(testBtn);
        s.Children.Add(row);

        _camTestText = new TextBlock { FontSize = 11, Foreground = Brush.Parse("#999999"), Margin = new Thickness(0, 4, 0, 0) };
        s.Children.Add(_camTestText);
        return s;
    }

    private StackPanel BuildSensitivitySection()
    {
        var s = Section("偷窥灵敏度（距离 / 角度）");
        _sensComboBox = new ComboBox { Width = 200 };
        _sensComboBox.Items.Add("低（较宽松）");
        _sensComboBox.Items.Add("中（推荐）");
        _sensComboBox.Items.Add("高（最严格）");
        _sensComboBox.SelectedIndex = Math.Clamp(S.Sensitivity, 0, 2);
        _sensComboBox.SelectionChanged += (_, _) =>
        {
            S.Sensitivity = _sensComboBox.SelectedIndex;
            S.Save();
        };
        s.Children.Add(_sensComboBox);
        s.Children.Add(new TextBlock
        {
            FontSize = 11, Foreground = Brush.Parse("#999999"), Margin = new Thickness(0, 4, 0, 0),
            Text = "档位越高，判定偷窥的距离更近、偏航角更小，误报更少但可能漏报。"
        });
        return s;
    }

    private StackPanel BuildActionsSection()
    {
        var s = Section("触发后的防护动作（可自定义）");
        s.Children.Add(MakeCheck("通过 ClassIsland 顶部提醒通知（任何场景都显示）", S.EnableTopBanner, v => { S.EnableTopBanner = v; Commit(); }));
        s.Children.Add(MakeCheck("受保护应用前台时全屏置顶保护（点击 / 空格 / 回车 关闭）", S.EnableFullscreenProtect, v => { S.EnableFullscreenProtect = v; Commit(); }));
        s.Children.Add(MakeCheck("扬声器短促提醒音", S.ActionSound, v => { S.ActionSound = v; Commit(); }));
        s.Children.Add(MakeCheck("最小化受保护隐私软件", S.ActionMinimize, v => { S.ActionMinimize = v; Commit(); }));
        return s;
    }

    private StackPanel BuildProtectSection()
    {
        var s = Section("受保护程序 / 窗口（仅查看这些时才触发）");
        s.Children.Add(MakeCheck("仅当受保护程序处于前台时启用识别（其余普通软件不触发 / 失焦暂停）",
            S.OnlyProtectForeground, v => { S.OnlyProtectForeground = v; Commit(); }));

        s.Children.Add(new TextBlock
        {
            Text = "进程名（exe）— 勾选框可单独启用 / 关闭该程序的保护",
            FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#555555"),
            Margin = new Thickness(0, 6, 0, 2)
        });
        _procHost = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var p in S.ProtectedProcesses) _procList.Add(p);
        s.Children.Add(_procHost);
        RebuildProcList();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        _procInput = new TextBox { Width = 200, Watermark = "例如 WeChat.exe" };
        var addBtn = MakeButton("添加", (_) =>
        {
            var input = _procInput;
            var t = (input.Text ?? string.Empty).Trim();
            if (t.Length == 0) return;
            if (!_procList.Any(x => string.Equals(x.Name, t, StringComparison.OrdinalIgnoreCase)))
            {
                _procList.Add(new ProtectedEntry { Name = t, Enabled = true });
                SyncProc(); S.Save(); RebuildProcList();
            }
            input.Text = "";
        });
        row.Children.Add(_procInput);
        row.Children.Add(addBtn);
        s.Children.Add(row);
        s.Children.Add(new TextBlock
        {
            FontSize = 11, Foreground = Brush.Parse("#999999"), Margin = new Thickness(0, 4, 0, 0),
            Text = "支持微信/QQ/浏览器/支付类网页/聊天软件等 exe 进程名（不区分大小写）。想覆盖所有文件夹/桌面窗口，可加入 explorer.exe。"
        });

        s.Children.Add(new Border
        {
            BorderBrush = Brush.Parse("#EEEEEE"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 10, 0, 4)
        });

        s.Children.Add(new TextBlock
        {
            Text = "窗口标题关键字（匹配桌面、文件夹等具体窗口）— 可单独启用 / 关闭",
            FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#555555"),
            Margin = new Thickness(0, 4, 0, 4)
        });

        _titleHost = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var t in S.ProtectedWindowTitles) _titleList.Add(t);
        s.Children.Add(_titleHost);
        RebuildTitleList();

        var tRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        _titleInput = new TextBox { Width = 200, Watermark = "例如 桌面 / 下载 / 私人" };
        var tAdd = MakeButton("添加", (_) =>
        {
            var input = _titleInput;
            var t = (input.Text ?? string.Empty).Trim();
            if (t.Length == 0) return;
            if (!_titleList.Any(x => string.Equals(x.Name, t, StringComparison.OrdinalIgnoreCase)))
            {
                _titleList.Add(new ProtectedEntry { Name = t, Enabled = true });
                SyncTitle(); S.Save(); RebuildTitleList();
            }
            input.Text = "";
        });
        tRow.Children.Add(_titleInput);
        tRow.Children.Add(tAdd);
        s.Children.Add(tRow);
        s.Children.Add(new TextBlock
        {
            FontSize = 11, Foreground = Brush.Parse("#999999"), Margin = new Thickness(0, 4, 0, 0),
            Text = "前台窗口标题（如文件夹名、桌面）包含此处任意关键字即触发（不区分大小写）。默认已含“桌面”。"
        });
        return s;
    }

    private Grid MakeEntryRow(ProtectedEntry entry, Action onRemove)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cb = new CheckBox
        {
            IsChecked = entry.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        cb.IsCheckedChanged += (_, _) => { entry.Enabled = cb.IsChecked == true; S.Save(); };
        Grid.SetColumn(cb, 0);

        var tb = new TextBlock
        {
            Text = entry.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        };
        Grid.SetColumn(tb, 1);

        var del = MakeButton("删除", (_) => onRemove());
        Grid.SetColumn(del, 2);

        grid.Children.Add(cb);
        grid.Children.Add(tb);
        grid.Children.Add(del);
        return grid;
    }

    private void RebuildProcList()
    {
        if (_procHost == null) return;
        _procHost.Children.Clear();
        foreach (var e in _procList)
        {
            _procHost.Children.Add(MakeEntryRow(e, () =>
            {
                _procList.Remove(e); SyncProc(); S.Save(); RebuildProcList();
            }));
        }
        if (_procList.Count == 0)
            _procHost.Children.Add(new TextBlock { Text = "（暂无，添加后此处显示）", FontSize = 11, Foreground = Brush.Parse("#BBBBBB"), Margin = new Thickness(2, 2, 0, 2) });
    }

    private void RebuildTitleList()
    {
        if (_titleHost == null) return;
        _titleHost.Children.Clear();
        foreach (var e in _titleList)
        {
            _titleHost.Children.Add(MakeEntryRow(e, () =>
            {
                _titleList.Remove(e); SyncTitle(); S.Save(); RebuildTitleList();
            }));
        }
        if (_titleList.Count == 0)
            _titleHost.Children.Add(new TextBlock { Text = "（暂无，添加后此处显示）", FontSize = 11, Foreground = Brush.Parse("#BBBBBB"), Margin = new Thickness(2, 2, 0, 2) });
    }

    private void SyncProc() => S.ProtectedProcesses = _procList.ToList();
    private void SyncTitle() => S.ProtectedWindowTitles = _titleList.ToList();

    private StackPanel BuildSuppressSection()
    {
        var s = Section("误触抑制");
        s.Children.Add(MakeCheck("暗光增强（提升暗光下检出率）", S.LowLightEnhance, v => { S.LowLightEnhance = v; Commit(); }));
        s.Children.Add(MakeCheck("镜子反光 / 海报人脸过滤（降低误识别）", S.MirrorPosterFilter, v => { S.MirrorPosterFilter = v; Commit(); }));
        return s;
    }

    private StackPanel BuildAdvancedSection()
    {
        var s = Section("高级选项");
        s.Children.Add(MakeCheck("启用快捷键一键开关智能防窥", S.EnableHotkey, v => { S.EnableHotkey = v; Commit(); }));
        var hkRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        hkRow.Children.Add(new TextBlock { Text = "修饰键", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        _hkModBox = new TextBox { Text = S.HotkeyModifiers, Width = 120 };
        hkRow.Children.Add(_hkModBox);
        hkRow.Children.Add(new TextBlock { Text = "主键", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        _hkKeyBox = new TextBox { Text = S.HotkeyKey, Width = 80 };
        hkRow.Children.Add(_hkKeyBox);
        var hkApply = MakeButton("应用快捷键", (_) =>
        {
            S.HotkeyModifiers = _hkModBox!.Text.Trim();
            S.HotkeyKey = _hkKeyBox!.Text.Trim();
            S.Save(); _engine.ApplySettings();
        });
        hkRow.Children.Add(hkApply);
        s.Children.Add(hkRow);
        s.Children.Add(new TextBlock
        {
            FontSize = 11, Foreground = Brush.Parse("#999999"), Margin = new Thickness(0, 2, 0, 0),
            Text = "快捷键用于一键暂停/恢复防护（等同于托盘菜单的暂停/恢复）。修饰键填 Ctrl+Shift / Ctrl / Alt 等；主键填单个字母，如 P。"
        });
        s.Children.Add(MakeCheck("自动保存截屏到本地日志（偷窥截图+调试帧；关闭后不保存任何图像）", S.ScreenshotOnPeek, v => { S.ScreenshotOnPeek = v; Commit(); }));
        s.Children.Add(MakeCheck("防护结束后自动恢复被最小化的窗口", S.RestoreOnSafe, v => { S.RestoreOnSafe = v; Commit(); }));
        s.Children.Add(MakeButton("立即清空陌生人提醒记录", (_) => _engine.ClearStrangerRecords()));
        s.Children.Add(new TextBlock
        {
            FontSize = 11, Foreground = Brush.Parse("#999999"), Margin = new Thickness(0, 2, 0, 0),
            Text = "陌生人提醒记录仅在内存中临时保存，退出 ClassIsland 或重新录入机主人脸后会自动清空，不会写入磁盘。"
        });
        return s;
    }

    private StackPanel BuildMasterSection()
    {
        var s = Section("总控");
        s.Children.Add(MakeCheck("随 ClassIsland 启动自动运行", S.AutoStartWithClassIsland, v => { S.AutoStartWithClassIsland = v; Commit(); }));
        s.Children.Add(MakeCheck("显示托盘图标（关闭后完全后台静默）", S.ShowTrayIcon, v => { S.ShowTrayIcon = v; Commit(); }));
        _enableSmartPeekCheck = MakeCheck("智能防窥总开关", S.EnableSmartPeek, v => { S.EnableSmartPeek = v; Commit(); });
        _pausedCheck = MakeCheck("暂停全部防护", S.Paused, v => { S.Paused = v; Commit(); });
        s.Children.Add(_enableSmartPeekCheck);
        s.Children.Add(_pausedCheck);
        s.Children.Add(MakeCheck("手动固定防窥（侧面视角变暗模糊，按 Esc 退出）", S.ManualMode, v => { S.ManualMode = v; Commit(); }));
        return s;
    }

    private static StackPanel Section(string title)
    {
        var s = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 4) };
        s.Children.Add(new Border
        {
            BorderBrush = Brush.Parse("#E0E0E0"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 4),
            Margin = new Thickness(0, 4, 0, 2),
            Child = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#555555") }
        });
        return s;
    }

    private CheckBox MakeCheck(string label, bool initial, Action<bool> onChange)
    {
        var cb = new CheckBox { Content = label, IsChecked = initial, Margin = new Thickness(0, 2, 0, 2) };
        cb.IsCheckedChanged += (_, _) => { if (!_updatingUi) onChange(cb.IsChecked == true); };
        return cb;
    }

    private static Button MakeButton(string label, Action<object?> onClick)
    {
        var b = new Button
        {
            Content = label, FontSize = 11, Padding = new Thickness(10, 4),
            Background = Brush.Parse("#E0E0E0"), CornerRadius = new CornerRadius(3)
        };
        b.Click += (_, _) => onClick(b);
        return b;
    }

    private void Commit() => _engine.ApplySettings();

    private void OnStatus(EngineStatus st) => Dispatcher.UIThread.Post(RefreshStatus);

    private void RefreshStatus()
    {
        var tb = _statusText;
        if (tb == null) return;
        var st = _engine.Status;
        var color = st switch
        {
            EngineStatus.Peek => "#F44336",
            EngineStatus.Secure => "#4CAF50",
            EngineStatus.Monitoring => "#2196F3",
            _ => "#FF9800"
        };
        var on = S.EnableSmartPeek ? "开" : "关";
        var paused = S.Paused ? "是" : "否";
        tb.Text = $"智能防窥：{on} ｜ 暂停：{paused} ｜ 状态：{PeekShieldEngine.StatusText(st)} ｜ 人脸数：{_engine.FaceCount} ｜ 已录入：{(_engine.IsEnrolled ? "是" : "否")}";
        tb.Foreground = Brush.Parse(color);
    }

    private void OnSettingsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _updatingUi = true;
            try
            {
                if (_enableSmartPeekCheck != null) _enableSmartPeekCheck.IsChecked = S.EnableSmartPeek;
                if (_pausedCheck != null) _pausedCheck.IsChecked = S.Paused;
                RefreshStatus();
            }
            finally { _updatingUi = false; }
        });
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _engine.StatusChanged -= OnStatus;
        _engine.SettingsChanged -= OnSettingsChanged;
        this.Unloaded -= OnUnloaded;
    }
}
