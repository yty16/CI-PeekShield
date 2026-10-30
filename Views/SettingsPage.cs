using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinForms = System.Windows.Forms;
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

    // 新增 UI 控件引用
    private TextBlock? _consentStatus;
    private CheckBox? _consentPolicyCheck;
    private CheckBox? _consentFaceCheck;
    private StackPanel? _wlHost;
    private TextBlock? _wlHint;
    private TextBox? _wlInput;
    private CheckBox? _wlEnabledCheck;
    private TextBlock? _bkStatus;
    private CheckBox? _bkSettingsCheck;
    private CheckBox? _bkOwnerCheck;
    private CheckBox? _bkWlCheck;
    private TextBox? _bkPwdBox;
    private TextBox? _impPwdBox;
    private StackPanel? _diagHost;
    private TextBlock? _diagTime;
    private TextBox? _logBox;
    private TextBox? _silentDirBox;
    private TextBox? _autoBkDirBox;
    private TextBox? _autoBkHoursBox;
    private TextBox? _autoBkPwdBox;

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
        RefreshConsentStatus();
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

        root.Children.Add(BuildConsentSection());
        root.Children.Add(BuildEnrollSection());
        root.Children.Add(BuildCameraSection());
        root.Children.Add(BuildSensitivitySection());
        root.Children.Add(BuildActionsSection());
        root.Children.Add(BuildProtectSection());
        root.Children.Add(BuildSuppressSection());
        root.Children.Add(BuildWhitelistSection());
        root.Children.Add(BuildBackupSection());
        root.Children.Add(BuildCaptureSection());
        root.Children.Add(BuildDiagnosticSection());
        root.Children.Add(BuildLogSection());
        root.Children.Add(BuildAdvancedSection());
        root.Children.Add(BuildMasterSection());

        scroll.Content = root;
        Content = scroll;
    }

    private StackPanel BuildConsentSection()
    {
        var s = Section("隐私同意（人脸处理前必须确认）");
        s.Children.Add(new TextBlock
        {
            FontSize = 11,
            Foreground = Brush.Parse("#999999"),
            TextWrapping = TextWrapping.Wrap,
            Text = "本插件在本地调用摄像头进行 AI 人脸检测与识别，用于区分机主与偷窥者。所有人脸特征与截图仅保存在本机插件目录，不会上传任何服务器。开始使用前，需阅读并同意以下告知。"
        });

        var policy = new Border
        {
            BorderBrush = Brush.Parse("#EEEEEE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 6, 0, 4),
            Child = new TextBlock
            {
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush.Parse("#666666"),
                Text = "· 人脸数据（特征向量、取证截图）仅存储于本机，不上传、不共享。\n" +
                       "· 仅在受保护程序/窗口前台时启用识别，失焦自动暂停。\n" +
                       "· 你可随时在下方撤回同意；撤回后人脸侦测将立即停止。\n" +
                       "· 详细隐私说明见项目仓库 PRIVACY 文档。"
            }
        };
        s.Children.Add(policy);

        _consentPolicyCheck = new CheckBox
        {
            Content = "我已阅读并理解上述隐私告知",
            Margin = new Thickness(0, 2, 0, 2)
        };
        s.Children.Add(_consentPolicyCheck);

        _consentFaceCheck = new CheckBox
        {
            Content = "单独同意进行人脸检测 / 识别处理（取消则停止所有识别）",
            IsChecked = true,
            Margin = new Thickness(0, 2, 0, 2)
        };
        s.Children.Add(_consentFaceCheck);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(MakeButton("确认同意并继续", (_) =>
        {
            if (_consentPolicyCheck?.IsChecked != true)
            {
                RefreshConsentStatus("请先勾选「我已阅读并理解上述隐私告知」");
                return;
            }
            ConsentService.SetConsent(S, true, _consentFaceCheck?.IsChecked == true);
            _engine.ApplySettings();
            RefreshConsentStatus("✓ 已记录你的同意，防护已可按设置运行");
        }));
        row.Children.Add(MakeButton("撤回同意", (_) =>
        {
            ConsentService.Revoke(S);
            _engine.ApplySettings();
            RefreshConsentStatus("已撤回同意，人脸侦测已停止");
        }));
        s.Children.Add(row);

        _consentStatus = new TextBlock
        {
            FontSize = 11,
            Foreground = Brush.Parse("#999999"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        s.Children.Add(_consentStatus);
        return s;
    }

    private void RefreshConsentStatus(string? extra = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_consentStatus == null) return;
            var need = ConsentService.NeedsConsent(S);
            var can = ConsentService.CanProcessFace(S);
            var baseMsg = need
                ? "⚠ 尚未获得完整同意，人脸侦测未启动。"
                : (can ? $"✓ 已同意（版本 {S.ConsentVersion}，时间 {S.ConsentTime}）" : "⚠ 人脸处理同意未开启。");
            _consentStatus.Text = string.IsNullOrEmpty(extra) ? baseMsg : extra;
            _consentStatus.Foreground = Brush.Parse(need ? "#E65100" : "#2E7D32");
        });
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
        var dlg = new WinForms.OpenFileDialog
        {
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*",
            Title = "选择一张包含你正脸的人脸照片"
        };
        if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
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
        s.Children.Add(MakeCheck("视线 / 低头检测（仅正对屏幕才视为注视，侧身 / 低头不触发）", S.EnableGazeDetection, v => { S.EnableGazeDetection = v; Commit(); }));
        s.Children.Add(new TextBlock
        {
            FontSize = 11, Foreground = Brush.Parse("#999999"), Margin = new Thickness(0, 2, 0, 0),
            Text = "开启后，除眼睛角度外还会估算头部偏航 / 俯仰：转头说话、低头看手机等场景不再误报偷窥。"
        });
        return s;
    }

    private StackPanel BuildWhitelistSection()
    {
        var s = Section("白名单（家人 / 常驻同事不触发）");
        _wlEnabledCheck = MakeCheck("启用白名单（名单内已录入人脸者不触发防窥）", S.WhitelistEnabled, v => { S.WhitelistEnabled = v; Commit(); });
        s.Children.Add(_wlEnabledCheck);

        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        _wlInput = new TextBox { Width = 200, Watermark = "成员名称，如 家人 / 同事A" };
        var addBtn = MakeButton("添加成员", (_) =>
        {
            var t = (_wlInput?.Text ?? string.Empty).Trim();
            if (t.Length == 0) return;
            _engine.AddWhitelistMember(t);
            _wlInput!.Text = "";
            RebuildWhitelistList();
        });
        addRow.Children.Add(_wlInput);
        addRow.Children.Add(addBtn);
        s.Children.Add(addRow);

        _wlHost = new StackPanel { Spacing = 2, Margin = new Thickness(0, 6, 0, 2) };
        s.Children.Add(_wlHost);
        RebuildWhitelistList();

        _wlHint = new TextBlock
        {
            FontSize = 11, Foreground = Brush.Parse("#999999"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        };
        s.Children.Add(_wlHint);

        s.Children.Add(new TextBlock
        {
            FontSize = 11, Foreground = Brush.Parse("#999999"), Margin = new Thickness(0, 2, 0, 0),
            Text = "添加成员后点击「录入人脸」让其正对摄像头完成录入；已录入者在场时不会触发防窥。"
        });
        return s;
    }

    private void RebuildWhitelistList()
    {
        if (_wlHost == null) return;
        _wlHost.Children.Clear();
        foreach (var e in _engine.GetWhitelist())
        {
            var id = e.Id;
            var name = e.Name;
            var enrolled = File.Exists(Path.Combine(PeekShieldSettings.PluginDir, "enrollment", "whitelist", e.Id, "embeddings.bin"));

            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tb = new TextBlock
            {
                Text = $"{name}　{(enrolled ? "✓ 已录入" : "未录入")}",
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tb, 0);

            var enrollBtn = MakeButton("录入人脸", (_) => _ = DoEnrollWhitelist(id, name));
            Grid.SetColumn(enrollBtn, 1);
            var delBtn = MakeButton("删除", (_) =>
            {
                _engine.RemoveWhitelist(id);
                RebuildWhitelistList();
            });
            Grid.SetColumn(delBtn, 2);

            grid.Children.Add(tb);
            grid.Children.Add(enrollBtn);
            grid.Children.Add(delBtn);
            _wlHost.Children.Add(grid);
        }
        if (_engine.GetWhitelist().Count == 0)
            _wlHost.Children.Add(new TextBlock { Text = "（暂无白名单成员，添加后此处显示）", FontSize = 11, Foreground = Brush.Parse("#BBBBBB"), Margin = new Thickness(2, 2, 0, 2) });
    }

    private async Task DoEnrollWhitelist(string id, string name)
    {
        UpdateWhitelistHint($"正在为「{name}」录入… 请正对摄像头保持静止");
        bool ok = await _engine.EnrollWhitelistAsync(id, name, 12, (n) => UpdateWhitelistHint($"「{name}」已采集 {n} 张样本…"));
        UpdateWhitelistHint(ok ? $"✓ 「{name}」录入成功" : $"✗ 「{name}」录入失败，请重试");
        RebuildWhitelistList();
    }

    private void UpdateWhitelistHint(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_wlHint == null) return;
            _wlHint.Text = text;
            _wlHint.Foreground = Brush.Parse(text.Contains("✓") ? "#2E7D32" : text.Contains("✗") ? "#C62828" : "#999999");
        });
    }

    private StackPanel BuildBackupSection()
    {
        var s = Section("配置备份与恢复（.kyd，轻量多配置载体）");
        s.Children.Add(new TextBlock
        {
            FontSize = 11, Foreground = Brush.Parse("#999999"), TextWrapping = TextWrapping.Wrap,
            Text = "将设置、机主人脸、白名单人脸导出为单个 .kyd 文件（可加密）。可用于迁移到其它电脑，或作为「办公 / 家庭」等多套配置切换的载体。"
        });

        _bkSettingsCheck = MakeCheck("包含设置", true, _ => { });
        _bkOwnerCheck = MakeCheck("包含机主人脸", S.IsEnrolled, _ => { });
        _bkWlCheck = MakeCheck("包含白名单人脸", S.Whitelist.Any(), _ => { });
        s.Children.Add(_bkSettingsCheck);
        s.Children.Add(_bkOwnerCheck);
        s.Children.Add(_bkWlCheck);

        var pwdRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        pwdRow.Children.Add(new TextBlock { Text = "导出密码（留空不加密）", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        _bkPwdBox = new TextBox { Width = 160, PasswordChar = '*' };
        pwdRow.Children.Add(_bkPwdBox);
        s.Children.Add(pwdRow);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(MakeButton("导出备份(.kyd)", (_) =>
        {
            var dlg = new WinForms.SaveFileDialog { Filter = ".kyd 备份|*.kyd|所有文件|*.*", Title = "导出 CI-PeekShield 配置备份" };
            if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
            var req = new BackupExportRequest
            {
                IncludeSettings = _bkSettingsCheck?.IsChecked == true,
                IncludeOwnerFace = _bkOwnerCheck?.IsChecked == true,
                IncludeWhitelistFaces = _bkWlCheck?.IsChecked == true,
                FilePassword = string.IsNullOrEmpty(_bkPwdBox?.Text) ? null : _bkPwdBox!.Text
            };
            var r = ConfigBackupService.Export(dlg.FileName, req);
            _bkStatus!.Text = r.Success
                ? $"✓ 已导出：{string.Join("、", r.Items)}（加密={r.Encrypted}）"
                : $"✗ 导出失败：{r.Error}";
            _bkStatus.Foreground = Brush.Parse(r.Success ? "#2E7D32" : "#C62828");
        }));
        row.Children.Add(MakeButton("导入备份(.kyd)", (_) =>
        {
            var dlg = new WinForms.OpenFileDialog { Filter = ".kyd 备份|*.kyd|所有文件|*.*", Title = "导入 CI-PeekShield 配置备份" };
            if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
            var path = dlg.FileName;
            string? pwd = string.IsNullOrEmpty(_impPwdBox?.Text) ? null : _impPwdBox!.Text;
            if (ConfigBackupService.FileRequiresPassword(path) && string.IsNullOrEmpty(pwd))
            {
                _bkStatus!.Text = "✗ 该备份已加密，请在下方「导入密码」填写后重试。";
                _bkStatus.Foreground = Brush.Parse("#C62828");
                return;
            }
            var (backup, err) = ConfigBackupService.Parse(path, pwd);
            if (backup == null)
            {
                _bkStatus!.Text = "✗ 读取失败：" + err;
                _bkStatus.Foreground = Brush.Parse("#C62828");
                return;
            }
            var r = ConfigBackupService.Apply(backup);
            if (r.Success)
            {
                // 重新加载机主人脸与白名单
                _engine.ReloadWhitelist();
                _engine.ApplySettings();
                RefreshStatus();
                RebuildWhitelistList();
                _bkStatus!.Text = $"✓ 已恢复：{string.Join("、", r.Items)}";
                _bkStatus.Foreground = Brush.Parse("#2E7D32");
            }
            else
            {
                _bkStatus!.Text = "✗ 恢复失败：" + r.Error;
                _bkStatus.Foreground = Brush.Parse("#C62828");
            }
        }));
        s.Children.Add(row);

        var impRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        impRow.Children.Add(new TextBlock { Text = "导入密码（仅加密备份需要）", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        _impPwdBox = new TextBox { Width = 160, PasswordChar = '*' };
        impRow.Children.Add(_impPwdBox);
        s.Children.Add(impRow);

        _bkStatus = new TextBlock { FontSize = 11, Foreground = Brush.Parse("#999999"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        s.Children.Add(_bkStatus);
        return s;
    }

    private StackPanel BuildCaptureSection()
    {
        var s = Section("静默取证 + 自动定时备份");
        s.Children.Add(MakeCheck("静默取证：触发时裁剪陌生人面部存本机（logs/evidence，不上传）", S.SilentCaptureStrangers, v => { S.SilentCaptureStrangers = v; Commit(); }));

        var dirRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        _silentDirBox = new TextBox { Width = 280, Text = S.SilentCaptureDir, Watermark = "留空使用默认 logs/evidence" };
        var pickSilent = MakeButton("选择目录", (_) =>
        {
            var d = new WinForms.FolderBrowserDialog { Description = "选择陌生人取证保存目录" };
            if (d.ShowDialog() == WinForms.DialogResult.OK) { _silentDirBox!.Text = d.SelectedPath; S.SilentCaptureDir = d.SelectedPath; S.Save(); }
        });
        dirRow.Children.Add(_silentDirBox);
        dirRow.Children.Add(pickSilent);
        s.Children.Add(dirRow);

        s.Children.Add(new Border { BorderBrush = Brush.Parse("#EEEEEE"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8, 0, 4) });

        s.Children.Add(MakeCheck("自动定时备份配置到 .kyd（轻量多配置）", S.AutoBackupEnabled, v => { S.AutoBackupEnabled = v; Commit(); }));

        var hoursRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        hoursRow.Children.Add(new TextBlock { Text = "备份间隔（小时）", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        _autoBkHoursBox = new TextBox { Width = 80, Text = S.AutoBackupIntervalHours.ToString() };
        hoursRow.Children.Add(_autoBkHoursBox);
        var applyHours = MakeButton("应用间隔", (_) =>
        {
            if (int.TryParse(_autoBkHoursBox?.Text, out var h) && h > 0) { S.AutoBackupIntervalHours = h; S.Save(); }
        });
        hoursRow.Children.Add(applyHours);
        s.Children.Add(hoursRow);

        var bkDirRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        _autoBkDirBox = new TextBox { Width = 280, Text = S.AutoBackupDir, Watermark = "留空使用默认 logs/backups" };
        var pickBk = MakeButton("选择目录", (_) =>
        {
            var d = new WinForms.FolderBrowserDialog { Description = "选择自动备份目录" };
            if (d.ShowDialog() == WinForms.DialogResult.OK) { _autoBkDirBox!.Text = d.SelectedPath; S.AutoBackupDir = d.SelectedPath; S.Save(); }
        });
        bkDirRow.Children.Add(_autoBkDirBox);
        bkDirRow.Children.Add(pickBk);
        s.Children.Add(bkDirRow);

        var pwdRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        pwdRow.Children.Add(new TextBlock { Text = "备份加密密码（留空不加密）", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        _autoBkPwdBox = new TextBox { Width = 160, Text = S.AutoBackupPassword, PasswordChar = '*' };
        var applyPwd = MakeButton("应用密码", (_) => { S.AutoBackupPassword = _autoBkPwdBox?.Text ?? ""; S.Save(); });
        pwdRow.Children.Add(_autoBkPwdBox);
        pwdRow.Children.Add(applyPwd);
        s.Children.Add(pwdRow);
        return s;
    }

    private StackPanel BuildDiagnosticSection()
    {
        var s = Section("诊断自检");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(MakeButton("运行诊断自检", (_) =>
        {
            try
            {
                var items = _engine.RunDiagnostic();
                _diagHost?.Children.Clear();
                foreach (var it in items)
                {
                    var color = it.Status switch
                    {
                        DiagnosticStatus.Error => "#C62828",
                        DiagnosticStatus.Warn => "#E65100",
                        _ => "#2E7D32"
                    };
                    var tb = new TextBlock
                    {
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 0),
                        Foreground = Brush.Parse(color),
                        Text = $"{(it.Status == DiagnosticStatus.Ok ? "✓" : it.Status == DiagnosticStatus.Warn ? "⚠" : "✗")} {it.Name}：{it.Detail}"
                    };
                    _diagHost?.Children.Add(tb);
                }
                if (_diagHost != null && items.Count == 0)
                    _diagHost.Children.Add(new TextBlock { Text = "（无项目）", FontSize = 11, Foreground = Brush.Parse("#BBBBBB") });
            }
            catch (Exception ex)
            {
                if (_diagHost != null) _diagHost.Children.Clear();
                _diagHost?.Children.Add(new TextBlock { Text = "诊断异常：" + ex.Message, FontSize = 12, Foreground = Brush.Parse("#C62828") });
            }
        }));
        s.Children.Add(row);

        _diagTime = new TextBlock { FontSize = 11, Foreground = Brush.Parse("#999999"), Margin = new Thickness(0, 4, 0, 0), Text = string.IsNullOrEmpty(S.LastDiagnosticAt) ? "尚未运行诊断" : $"上次诊断：{S.LastDiagnosticAt}" };
        s.Children.Add(_diagTime);

        _diagHost = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 0) };
        s.Children.Add(_diagHost);
        return s;
    }

    private StackPanel BuildLogSection()
    {
        var s = Section("日志查看器");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(MakeButton("刷新", (_) => RefreshLog()));
        row.Children.Add(MakeButton("打开日志目录", (_) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", LoggerService.LogDir); } catch { }
        }));
        row.Children.Add(MakeButton("清空日志", (_) =>
        {
            try
            {
                foreach (var f in new[] { "engine.log", "peek.log" })
                {
                    var p = Path.Combine(LoggerService.LogDir, f);
                    if (File.Exists(p)) File.Delete(p);
                }
                RefreshLog();
            }
            catch { }
        }));
        s.Children.Add(row);

        _logBox = new TextBox
        {
            FontFamily = FontFamily.Parse("Consolas, Microsoft YaHei, monospace"),
            FontSize = 11,
            Height = 200,
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };
        s.Children.Add(_logBox);
        RefreshLog();
        return s;
    }

    private void RefreshLog()
    {
        if (_logBox == null) return;
        var sb = new System.Text.StringBuilder();
        foreach (var f in new[] { "engine.log", "peek.log" })
        {
            var p = Path.Combine(LoggerService.LogDir, f);
            if (!File.Exists(p)) continue;
            sb.AppendLine($"===== {f} =====");
            var lines = File.ReadAllLines(p);
            var tail = lines.Skip(Math.Max(0, lines.Length - 200));
            foreach (var l in tail) sb.AppendLine(l);
        }
        if (sb.Length == 0) sb.AppendLine("（暂无日志）");
        _logBox.Text = sb.ToString();
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
            EngineStatus.PendingConsent => "#E65100",
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
                if (_wlEnabledCheck != null) _wlEnabledCheck.IsChecked = S.WhitelistEnabled;
                if (_bkOwnerCheck != null) _bkOwnerCheck.IsChecked = S.IsEnrolled;
                if (_bkWlCheck != null) _bkWlCheck.IsChecked = S.Whitelist.Any();
                if (_diagTime != null) _diagTime.Text = string.IsNullOrEmpty(S.LastDiagnosticAt) ? "尚未运行诊断" : $"上次诊断：{S.LastDiagnosticAt}";
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
