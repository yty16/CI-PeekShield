using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CIPeekShield.Models;
using OpenCvSharp;

namespace CIPeekShield.Services;

public enum EngineStatus
{
    Idle, Monitoring, Secure, Peek, Paused, Manual, NoCamera, NotEnrolled, Error
}

public class PeekShieldEngine
{
    public static readonly PeekShieldEngine Instance = new();

    private PeekShieldSettings _settings = new();
    private FaceRecognizer? _recognizer;
    private FaceVerifier? _verifier;
    private FaceEngine? _faceEngine;
    private readonly CameraService _camera = new();
    private readonly ForegroundWatcher _fg = new();
    private readonly OverlayService _overlay = new();
    private PeekShieldNotificationProvider? _notifier;
    private TrayService? _tray;
    private HotkeyService? _hotkey;
    private HotkeyService? _escHotkey;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private bool _peekActive;
    private int _faceCount;
    private bool _ownerPresent;
    private bool _loggedFrame;
    private EngineStatus _status = EngineStatus.Idle;
    private string _cameraError = "";

    private DateTime _lastStatusLog = DateTime.MinValue;
    private int _lastLoggedFaceCount = -1;
    private bool _lastLoggedOwnerPresent;
    private DateTime _lastDebugSave = DateTime.MinValue;
    private int _debugSavedCount;

    private readonly Queue<int> _faceCountHistory = new();
    private readonly Queue<bool> _ownerHistory = new();
    private readonly Queue<bool> _strangerHistory = new();
    private const int HistorySize = 5;

    private readonly List<StrangerRecord> _strangers = new();
    private const double StrangerMatchThreshold = 0.6;

    private class StrangerRecord
    {
        public float[] Embedding = Array.Empty<float>();
        public DateTime FirstSeen;
        public DateTime LastSeen;
        public int AlertCount;
        public DateTime LastAlertTime;
    }

    public event Action<EngineStatus>? StatusChanged;
    public event Action? SettingsChanged;

    public PeekShieldSettings Settings => _settings;
    public EngineStatus Status => _status;
    public int FaceCount => _faceCount;
    public bool OwnerPresent => _ownerPresent;
    public bool IsPeekActive => _peekActive;
    public string CameraError => _cameraError;
    public bool IsEnrolled => _verifier?.IsEnrolled ?? false;

    public double LastMatchDistance => _verifier?.LastDistance ?? -1;
    public double LastMatchThreshold => _verifier?.LastThreshold ?? -1;

    private static string EnrollDir => Path.Combine(PeekShieldSettings.PluginDir, "enrollment");

    public void SetNotifier(PeekShieldNotificationProvider? notifier)
    {
        _notifier = notifier;
    }

    public void Initialize()
    {
        _settings = PeekShieldSettings.Load();
        _recognizer = new FaceRecognizer();
        var modelsDir = Path.Combine(PeekShieldSettings.PluginDir, "Assets", "Models");
        var spPath = Path.Combine(modelsDir, "shape_predictor_68_face_landmarks.dat");
        var netPath = Path.Combine(modelsDir, "dlib_face_recognition_resnet_model_v1.dat");
        if (!File.Exists(spPath) || !File.Exists(netPath))
        {
            _cameraError = "人脸识别模型文件缺失，请重新构建插件";
            LoggerService.LogInfo("Dlib 模型文件缺失：sp=" + spPath + " net=" + netPath);
            PushStatus(EngineStatus.Error);
        }
        else
        {
            _recognizer.Load(spPath, netPath);
        }
        _verifier = new FaceVerifier();
        _verifier.Load(EnrollDir);
        _faceEngine = new FaceEngine(_recognizer, _verifier);
        if (!_faceEngine.IsFaceReady)
        {
            _cameraError = "人脸识别模型未能加载，详见 logs/engine.log";
            LoggerService.LogInfo("人脸识别引擎未就绪：recognizer.IsReady=" + _recognizer.IsReady);
            PushStatus(EngineStatus.Error);
        }

        _fg.Start();
        _fg.StateChanged += () => {  _fg.RefreshForeground(); };

        _overlay.Dismissed += OnOverlayDismissed;

        _tray = new TrayService();
        _tray.OnTogglePause += TogglePause;
        _tray.OnToggleManual += ToggleManual;
        _tray.OnOpenSettings += TrayService.OpenSettingsPage;
        _tray.OnHideTray += () =>
        {
            _settings.ShowTrayIcon = false;
            _settings.Save();
            _tray?.Hide();
        };
        if (_settings.ShowTrayIcon) _tray.Start();

        ApplyHotkey();
        ApplyEscHotkey();
        ApplyManualMode();

        LoggerService.LogInfo("引擎初始化完成（构建签名 " + PeekShieldSettings.BuildSignature + "）");
        PushStatus(_settings.Paused ? EngineStatus.Paused
            : (_settings.ManualMode ? EngineStatus.Manual
            : (_verifier.IsEnrolled ? EngineStatus.Monitoring : EngineStatus.NotEnrolled)));

        if (_settings.EnableSmartPeek && !_settings.Paused)
            StartLoop();
    }

    public void StartLoop()
    {
        if (_loopTask != null && !_loopTask.IsCompleted) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loopTask = Task.Run(() => LoopAsync(ct));
    }

    public void StopLoop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _loopTask?.Wait(2000); } catch { }
        _loopTask = null;
        _cts?.Dispose();
        _cts = null;
        if (_camera.IsOpen) _camera.Close();
    }

    public void RestartCamera()
    {
        _camera.Close();
        _loggedFrame = false;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var frame = new Mat();
        while (!ct.IsCancellationRequested)
        {
            if (_faceEngine == null) { await SafeDelay(2000, ct); continue; }
            bool should = ShouldMonitor();
            if (!should)
            {
                if (_camera.IsOpen) _camera.Close();
                if (_peekActive) EndPeek();
                PushStatus(_settings.Paused ? EngineStatus.Paused
                    : (_settings.ManualMode ? EngineStatus.Manual
                    : (_verifier!.IsEnrolled ? EngineStatus.Monitoring : EngineStatus.NotEnrolled)));
                await SafeDelay(700, ct);
                continue;
            }

            if (!_camera.IsOpen)
            {
                if (!_camera.Open(_settings.CameraIndex))
                {
                    _cameraError = _camera.LastError ?? "摄像头打开失败";
                    PushStatus(EngineStatus.NoCamera);
                    await SafeDelay(3000, ct);
                    continue;
                }
                _cameraError = "";
            }

            if (!_camera.ReadFrame(frame) || frame.Empty())
            {
                await SafeDelay(120, ct);
                continue;
            }

            if (!_loggedFrame)
            {
                LoggerService.LogInfo($"摄像头就绪，帧尺寸={frame.Width}x{frame.Height}，人脸模型就绪={_faceEngine?.IsFaceReady}");
                _loggedFrame = true;
            }

            List<FaceInfo> faces = new();
            try
            {
                faces = _faceEngine!.Detect(frame, _settings.Sensitivity, _settings.LowLightEnhance, _settings.MirrorPosterFilter);
                _fg.RefreshForeground();
                Evaluate(faces, frame);
            }
            catch (Exception ex)
            {
                LoggerService.LogInfo("监控帧处理异常：" + ex.Message);
                await SafeDelay(1000, ct);
                continue;
            }

            int fps = faces.Count > 0 ? 12 : 2;
            await SafeDelay(1000 / fps, ct);
        }
    }

    private static async Task SafeDelay(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); } catch { }
    }

    private bool ShouldMonitor()
    {
        if (!_settings.EnableSmartPeek) return false;
        if (_settings.Paused) return false;
        if (_settings.ManualMode) return false;
        if (_fg.IsLocked) return false;
        if (_verifier == null || !_verifier.IsEnrolled) return false;
        if (_settings.OnlyProtectForeground && _settings.ProtectedProcesses.Count > 0 && !IsProtectedForeground())
            return false;
        return true;
    }

    private bool IsProtectedForeground()
    {
        var name = _fg.ForegroundProcessName;
        if (!string.IsNullOrEmpty(name))
        {
            var target = NormalizeProcessName(name);
            if (_settings.ProtectedProcesses.Any(p => p.Enabled && string.Equals(NormalizeProcessName(p.Name), target, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        var title = _fg.ForegroundWindowTitle;
        if (!string.IsNullOrWhiteSpace(title))
        {
            return _settings.ProtectedWindowTitles.Any(k =>
                k.Enabled && !string.IsNullOrWhiteSpace(k.Name) &&
                title.Contains(k.Name, StringComparison.OrdinalIgnoreCase));
        }
        return false;
    }

    private bool HasAlertableStranger(List<FaceInfo> strangers)
    {
        CleanupStrangers();
        int limit = StrangerAlertLimit();
        int coolMin = StrangerCooldownMinutes();
        bool any = false;
        foreach (var face in strangers)
        {
            var rec = FindOrCreateStranger(face.Embedding);
            if (rec.AlertCount >= limit)
            {
                if ((DateTime.Now - rec.LastAlertTime).TotalMinutes >= coolMin)
                    rec.AlertCount = 0;
                else
                    continue;
            }
            any = true;
        }
        return any;
    }

    private int StrangerAlertLimit()
    {
        var ns = _notifier?.Settings;
        int v = ns == null ? 2 : ns.StrangerAlertLimit;
        return Math.Clamp(v, 1, 20);
    }

    private int StrangerCooldownMinutes()
    {
        var ns = _notifier?.Settings;
        int v = ns == null ? 10 : ns.StrangerAlertCooldownMinutes;
        return Math.Clamp(v, 1, 1440);
    }

    private void RegisterStrangerAlert(List<FaceInfo> strangers)
    {
        CleanupStrangers();
        foreach (var face in strangers)
        {
            var rec = FindOrCreateStranger(face.Embedding);
            rec.AlertCount++;
            rec.LastAlertTime = DateTime.Now;
            rec.LastSeen = DateTime.Now;
        }
    }

    private StrangerRecord FindOrCreateStranger(float[] embedding)
    {
        double best = double.MaxValue;
        StrangerRecord? match = null;
        foreach (var r in _strangers)
        {
            double d = EmbDist(r.Embedding, embedding);
            if (d < best) { best = d; match = r; }
        }
        if (match != null && best < StrangerMatchThreshold)
        {
            match.LastSeen = DateTime.Now;
            return match;
        }
        var rec = new StrangerRecord
        {
            Embedding = (float[])embedding.Clone(),
            FirstSeen = DateTime.Now,
            LastSeen = DateTime.Now,
            AlertCount = 0,
            LastAlertTime = DateTime.Now
        };
        _strangers.Add(rec);
        return rec;
    }

    private void CleanupStrangers()
    {
        int coolMin = StrangerCooldownMinutes();
        var cutoff = DateTime.Now.AddMinutes(-Math.Max(coolMin, 30));
        _strangers.RemoveAll(r => r.LastSeen < cutoff);
    }

    public void ClearStrangerRecords()
    {
        _strangers.Clear();
        LoggerService.LogInfo("陌生人提醒记录已手动清空");
    }

    private static string NormalizeProcessName(string name)
    {
        if (name.Length > 4 && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return name[..^4];
        return name;
    }

    private void Evaluate(List<FaceInfo> faces, Mat frame)
    {
        int count = faces.Count;
        bool owner = faces.Any(f => f.IsOwner);
        bool strangerLooking = faces.Any(f => !f.IsOwner && f.LookingAtScreen);

        PushHistory(count, owner, strangerLooking);
        int stableCount = StableFaceCount();
        bool stableOwner = StableOwner();
        bool stableStranger = StableStranger();

        _faceCount = stableCount;
        _ownerPresent = stableOwner;

        var strangerFaces = faces.Where(f => !f.IsOwner && f.LookingAtScreen && f.Embedding.Length == FaceVerifier.Dim).ToList();
        bool hasAlertableStranger = stableCount >= 1 && stableStranger && HasAlertableStranger(strangerFaces);

        if (hasAlertableStranger && !_peekActive) StartPeek(frame, strangerFaces);
        else if (!hasAlertableStranger && _peekActive) EndPeek();

        if (!hasAlertableStranger)
        {
            var st = stableCount == 0 ? EngineStatus.Monitoring
                : (stableOwner && stableCount == 1 ? EngineStatus.Secure : EngineStatus.Monitoring);
            PushStatus(st);
        }

        double dist = _verifier?.LastDistance ?? -1;
        double thr = _verifier?.LastThreshold ?? -1;
        bool changed = stableCount != _lastLoggedFaceCount || stableOwner != _lastLoggedOwnerPresent;
        bool periodic = (DateTime.Now - _lastStatusLog).TotalSeconds > 1.5;
        if (periodic || changed)
        {
            LoggerService.LogInfo($"帧诊断 人脸={stableCount} 原始检测={_faceEngine?.LastRawFaceCount ?? -1} 灰度均值={_faceEngine?.LastFrameMean ?? -1:F1} 标准差={_faceEngine?.LastFrameStd ?? -1:F1} 机主={stableOwner} 陌生人注视={stableStranger} 最近距离={dist:F3} 阈值={thr:F3} 离散度={_verifier?.SelfGap ?? -1:F3}");
            _lastStatusLog = DateTime.Now;
            _lastLoggedFaceCount = stableCount;
            _lastLoggedOwnerPresent = stableOwner;
        }

        if (stableCount == 0 && _debugSavedCount < 3 && (DateTime.Now - _lastDebugSave).TotalSeconds > 8)
        {
            try
            {
                LoggerService.SaveDebugFrame(frame, _settings);
                _lastDebugSave = DateTime.Now;
                _debugSavedCount++;
                LoggerService.LogInfo($"已保存调试帧 logs/debug_frame.jpg（灰度均值={_faceEngine?.LastFrameMean ?? -1:F1} 标准差={_faceEngine?.LastFrameStd ?? -1:F1}），便于排查检测失败");
            }
            catch { }
        }
    }

    private void PushHistory(int count, bool owner, bool stranger)
    {
        _faceCountHistory.Enqueue(count);
        _ownerHistory.Enqueue(owner);
        _strangerHistory.Enqueue(stranger);
        while (_faceCountHistory.Count > HistorySize) _faceCountHistory.Dequeue();
        while (_ownerHistory.Count > HistorySize) _ownerHistory.Dequeue();
        while (_strangerHistory.Count > HistorySize) _strangerHistory.Dequeue();
    }

    private int StableFaceCount() => _faceCountHistory.Count == 0 ? 0 : _faceCountHistory.Max();
    private bool StableOwner() => _ownerHistory.Count(h => h) >= 3;
    private bool StableStranger() => _strangerHistory.Count(h => h) >= 3;

    private void StartPeek(Mat frame, List<FaceInfo> strangers)
    {
        _peekActive = true;
        RegisterStrangerAlert(strangers);

        if (_settings.EnableTopBanner)
            _notifier?.PushPeekAlert("CI-PeekShield 已检测到他人注视屏幕，请注意隐私。");

        if (_settings.EnableFullscreenProtect && IsProtectedForeground())
            _overlay.ShowProtect();

        if (_settings.ActionSound) AudioAlert.Play();
        if (_settings.ActionMinimize) WindowGuard.MinimizeProcesses(_settings.ProtectedProcesses);

        LoggerService.LogPeek(_settings, _faceCount);
        LoggerService.SaveSnapshot(frame, _settings);
        PushStatus(EngineStatus.Peek);
    }

    private void EndPeek()
    {
        _peekActive = false;
        _overlay.HideAll();
        if (_settings.RestoreOnSafe) WindowGuard.RestoreProcesses(_settings.ProtectedProcesses);
        PushStatus(_ownerPresent ? EngineStatus.Secure : EngineStatus.Monitoring);
    }

    private void OnOverlayDismissed()
    {
        if (_peekActive) EndPeek();
    }

    public void ApplySettings()
    {
        if (_tray != null)
        {
            if (_settings.ShowTrayIcon) _tray.Start();
            else _tray.Hide();
            _tray.SetPauseLabel(_settings.Paused);
            _tray.SetManualLabel(_settings.ManualMode);
        }
        ApplyHotkey();
        ApplyEscHotkey();
        ApplyManualMode();

        if (_settings.EnableSmartPeek && !_settings.Paused)
            StartLoop();
        else
            StopLoop();
    }

    private void ApplyManualMode()
    {
        if (_settings.Paused)
        {
            _overlay.HideAll();
            return;
        }
        if (_settings.ManualMode && _settings.EnableSmartPeek)
        {
            if (_peekActive) EndPeek();
            _overlay.ShowFog("手动防窥模式已开启（侧面视角已变暗模糊）");
            PushStatus(EngineStatus.Manual);
            return;
        }
        _overlay.HideAll();
    }

    private void ApplyHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        if (!_settings.EnableHotkey) return;
        Keys mod = 0;
        var m = _settings.HotkeyModifiers ?? "";
        if (m.Contains("Ctrl", StringComparison.OrdinalIgnoreCase)) mod |= Keys.Control;
        if (m.Contains("Shift", StringComparison.OrdinalIgnoreCase)) mod |= Keys.Shift;
        if (m.Contains("Alt", StringComparison.OrdinalIgnoreCase)) mod |= Keys.Alt;
        if (!Enum.TryParse<Keys>(_settings.HotkeyKey, true, out var key)) key = Keys.P;
        var desc = string.Join("+", new[] { (mod & Keys.Control) != 0 ? "Ctrl" : null, (mod & Keys.Shift) != 0 ? "Shift" : null, (mod & Keys.Alt) != 0 ? "Alt" : null, key.ToString() }.Where(s => s != null));
        _hotkey = new HotkeyService(mod, key, OnHotkeyPressed);
        LoggerService.LogInfo($"快捷键已注册：{desc}，用于一键暂停/恢复防护");
    }

    private void ApplyEscHotkey()
    {
        _escHotkey?.Dispose();
        _escHotkey = new HotkeyService(0, Keys.Escape, OnEscPressed);
    }

    private void OnEscPressed()
    {
        if (!_settings.ManualMode) return;
        ExitManual();
    }

    public void ExitManual()
    {
        if (!_settings.ManualMode) return;
        _settings.ManualMode = false;
        _settings.Save();
        ApplySettings();
        SettingsChanged?.Invoke();
        _tray?.ShowBalloon("CI-PeekShield", "已退出手动防窥", ToolTipIcon.Info);
    }

    private void OnHotkeyPressed()
    {
        if (!_settings.EnableSmartPeek)
        {
            LoggerService.LogInfo("快捷键触发：智能防窥总开关未开启，无动作");
            _tray?.ShowBalloon("CI-PeekShield", "智能防窥总开关未开启，快捷键无效", ToolTipIcon.Warning);
            return;
        }
        TogglePause();
        var state = _settings.Paused ? "已暂停" : "已恢复";
        LoggerService.LogInfo($"快捷键切换防护状态：{state}");
        _tray?.ShowBalloon("CI-PeekShield", $"智能防窥{state}", ToolTipIcon.Info);
    }

    public void TogglePause()
    {
        _settings.Paused = !_settings.Paused;
        _settings.Save();
        ApplySettings();
        SettingsChanged?.Invoke();
    }

    public void ToggleManual()
    {
        _settings.ManualMode = !_settings.ManualMode;
        _settings.Save();
        ApplySettings();
        SettingsChanged?.Invoke();
    }

    public void ToggleSmartPeek()
    {
        _settings.EnableSmartPeek = !_settings.EnableSmartPeek;
        _settings.Save();
        ApplySettings();
        SettingsChanged?.Invoke();
    }

    public void SetEnabled(bool enabled)
    {
        _settings.EnableSmartPeek = enabled;
        _settings.Save();
        ApplySettings();
        SettingsChanged?.Invoke();
    }

    public async Task<bool> EnrollAsync(int samples = 10, Action<int>? progress = null)
    {
        StopLoop();
        bool wasEnrolled = _verifier!.IsEnrolled;
        _verifier.Clear();
        bool ok = false;
        var seen = new List<float[]>();
        try
        {
            using var cam = new CameraService();
            if (!cam.Open(_settings.CameraIndex))
            {
                _cameraError = cam.LastError ?? "摄像头打开失败";
                RestorePriorEnrollment(wasEnrolled);
                return false;
            }
            using var frame = new Mat();
            int collected = 0;
            int attempts = 0;
            for (int i = 0; i < samples + 30 && collected < samples; i++)
            {
                if (!cam.ReadFrame(frame) || frame.Empty()) { await SafeDelay(150, default); continue; }
                var faces = _recognizer!.Detect(frame);
                if (faces.Count == 0) { await SafeDelay(200, default); continue; }
                attempts++;
                var emb = faces[0].Embedding;
                bool dup = seen.Any(s => EmbDist(emb, s) < 0.25);
                if (!dup)
                {
                    seen.Add(emb);
                    if (_verifier.AddSample(emb)) { collected++; progress?.Invoke(collected); }
                }
                await SafeDelay(300, default);
            }
            cam.Close();
            ok = collected >= 3;
            if (ok)
            {
                _verifier.Save(EnrollDir);
                _settings.IsEnrolled = true;
                LoggerService.LogInfo($"摄像头录入成功：尝试={attempts} 接受={collected} 离散度={_verifier.SelfGap:F3} 灵敏度={_settings.Sensitivity}");
            }
            else
            {
                RestorePriorEnrollment(wasEnrolled);
                LoggerService.LogInfo($"摄像头录入失败：尝试={attempts} 接受={collected}（需至少 3 张合格样本{(wasEnrolled ? "，已恢复此前录入" : "")}）");
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("录入过程异常：" + ex);
            RestorePriorEnrollment(wasEnrolled);
            ok = false;
        }
        finally
        {
            ResetHistory();
            _settings.Save();
            if (_settings.EnableSmartPeek && !_settings.Paused) StartLoop();
            PushStatus(_settings.IsEnrolled ? EngineStatus.Monitoring : EngineStatus.NotEnrolled);
        }
        return ok;
    }

    private static double EmbDist(float[] a, float[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) { double d = a[i] - b[i]; s += d * d; }
        return Math.Sqrt(s);
    }

    private void RestorePriorEnrollment(bool wasEnrolled)
    {
        if (wasEnrolled)
        {
            try { _verifier!.Load(EnrollDir); } catch { }
            _settings.IsEnrolled = _verifier!.IsEnrolled;
        }
        else
        {
            _verifier!.Clear();
            _settings.IsEnrolled = false;
        }
    }

    public async Task<bool> EnrollFromPhotoAsync(string imagePath, Action<int>? progress = null)
    {
        StopLoop();
        bool wasEnrolled = _verifier!.IsEnrolled;
        _verifier.Clear();
        bool ok = false;
        try
        {
            if (!File.Exists(imagePath)) { RestorePriorEnrollment(wasEnrolled); return false; }
            using var img = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (img.Empty()) { RestorePriorEnrollment(wasEnrolled); return false; }

            int collected = 0;
            var faces = _recognizer!.Detect(img);
            if (faces.Count == 0)
            {
                RestorePriorEnrollment(wasEnrolled);
                LoggerService.LogInfo("照片录入失败：未在照片中检测到人脸，请换一张正脸清晰照片");
                return false;
            }
            if (_verifier.AddSample(faces[0].Embedding)) collected++;
            using var flip = new Mat(); Cv2.Flip(img, flip, FlipMode.Y);
            var f2 = _recognizer.Detect(flip);
            if (f2.Count > 0 && _verifier.AddSample(f2[0].Embedding)) collected++;

            ok = collected >= 1;
            if (ok)
            {
                _verifier.Save(EnrollDir);
                _settings.IsEnrolled = true;
                LoggerService.LogInfo($"照片录入成功：接受={collected} 离散度={_verifier.SelfGap:F3} 灵敏度={_settings.Sensitivity}");
            }
            else
            {
                RestorePriorEnrollment(wasEnrolled);
                LoggerService.LogInfo($"照片录入失败：接受={collected}（请换一张正脸清晰照片{(wasEnrolled ? "，已恢复此前录入" : "")}）");
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("录入过程异常：" + ex);
            RestorePriorEnrollment(wasEnrolled);
            ok = false;
        }
        finally
        {
            ResetHistory();
            _settings.Save();
            if (_settings.EnableSmartPeek && !_settings.Paused) StartLoop();
            PushStatus(_settings.IsEnrolled ? EngineStatus.Monitoring : EngineStatus.NotEnrolled);
        }
        return ok;
    }

    public void ClearEnrollment()
    {
        _verifier?.Clear();
        try
        {
            if (Directory.Exists(EnrollDir))
                foreach (var f in Directory.GetFiles(EnrollDir)) File.Delete(f);
        }
        catch { }
        _settings.IsEnrolled = false;
        _settings.Save();
        ResetHistory();
        PushStatus(_peekActive ? EngineStatus.Peek : EngineStatus.NotEnrolled);
    }

    private void ResetHistory()
    {
        _faceCountHistory.Clear();
        _ownerHistory.Clear();
        _strangerHistory.Clear();
    }

    private void PushStatus(EngineStatus s)
    {
        _status = s;
        _tray?.SetTooltip("CI-PeekShield · " + StatusText(s) + " · 人脸 " + _faceCount);
        StatusChanged?.Invoke(s);
    }

    public static string StatusText(EngineStatus s) => s switch
    {
        EngineStatus.Monitoring => "监控中",
        EngineStatus.Secure => "安全（仅机主）",
        EngineStatus.Peek => "⚠ 检测到偷窥",
        EngineStatus.Paused => "已暂停",
        EngineStatus.Manual => "手动防窥",
        EngineStatus.NoCamera => "摄像头不可用",
        EngineStatus.NotEnrolled => "未录入人脸",
        EngineStatus.Error => "错误",
        _ => "就绪"
    };

    public void Dispose()
    {
        StopLoop();
        _fg.Stop();
        _overlay.Dismissed -= OnOverlayDismissed;
        _overlay.HideAll();
        _hotkey?.Dispose();
        _escHotkey?.Dispose();
        _tray?.Stop();
        _recognizer?.Dispose();
        _faceEngine?.Dispose();
        _verifier?.Dispose();
    }
}
