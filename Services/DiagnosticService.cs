using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using CIPeekShield.Models;

namespace CIPeekShield.Services;

public enum DiagnosticStatus { Ok, Warn, Error }

public class DiagnosticItem
{
    public string Name { get; set; } = "";
    public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Ok;
    public string Detail { get; set; } = "";
}

public static class DiagnosticService
{
    public static List<DiagnosticItem> Run(PeekShieldSettings settings, PeekShieldEngine? engine)
    {
        var items = new List<DiagnosticItem>();
        items.Add(CheckRuntime());
        items.Add(CheckModels());
        items.Add(CheckCamera(engine));
        items.Add(CheckFaceData(settings, engine));
        items.Add(CheckAutostart(settings));
        items.Add(CheckSettingsConsistency(settings));
        items.Add(CheckDataDir());
        items.Add(CheckConfigBackup());
        return items;
    }

    private static string OsLabel()
    {
        string name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Unknown";
        return $"{name} {Environment.OSVersion.Version}";
    }

    private static DiagnosticItem CheckRuntime()
    {
        var detail = $"{OsLabel()} · .NET {Environment.Version.Major}.{Environment.Version.Minor}";
        return new DiagnosticItem { Name = "运行环境", Status = DiagnosticStatus.Ok, Detail = detail };
    }

    private static DiagnosticItem CheckModels()
    {
        var dir = Path.Combine(PeekShieldSettings.PluginDir, "Assets", "Models");
        var sp = Path.Combine(dir, "shape_predictor_68_face_landmarks.dat");
        var net = Path.Combine(dir, "dlib_face_recognition_resnet_model_v1.dat");
        var missing = new List<string>();
        if (!File.Exists(sp)) missing.Add("shape_predictor_68_face_landmarks.dat");
        if (!File.Exists(net)) missing.Add("dlib_face_recognition_resnet_model_v1.dat");
        if (missing.Count == 0)
            return new DiagnosticItem { Name = "人脸模型文件", Status = DiagnosticStatus.Ok, Detail = $"模型完整（{dir}）" };
        return new DiagnosticItem { Name = "人脸模型文件", Status = DiagnosticStatus.Error, Detail = "缺少模型文件：" + string.Join("、", missing) + "，人脸检测/识别将无法工作。" };
    }

    private static DiagnosticItem CheckCamera(PeekShieldEngine? engine)
    {
        var usingStates = new[]
        {
            EngineStatus.Monitoring, EngineStatus.Secure, EngineStatus.Peek,
            EngineStatus.Manual, EngineStatus.Paused
        };
        if (engine != null)
        {
            var st = engine.Status;
            if (st == EngineStatus.NoCamera)
                return new DiagnosticItem { Name = "摄像头", Status = DiagnosticStatus.Error, Detail = "未检测到可用摄像头或摄像头被占用，防偷窥将无法运行。" };
            if (usingStates.Contains(st))
                return new DiagnosticItem { Name = "摄像头", Status = DiagnosticStatus.Ok, Detail = "摄像头工作正常（引擎正在使用）。" };
        }
        try
        {
            var cams = CameraService.Enumerate();
            if (cams.Count > 0)
                return new DiagnosticItem { Name = "摄像头", Status = DiagnosticStatus.Ok, Detail = $"检测到 {cams.Count} 个摄像头设备。" };
            return new DiagnosticItem { Name = "摄像头", Status = DiagnosticStatus.Warn, Detail = "未检测到摄像头设备；若尚未录入人脸可暂时忽略。" };
        }
        catch (Exception ex)
        {
            return new DiagnosticItem { Name = "摄像头", Status = DiagnosticStatus.Warn, Detail = "摄像头检测异常：" + ex.Message };
        }
    }

    private static DiagnosticItem CheckFaceData(PeekShieldSettings settings, PeekShieldEngine? engine)
    {
        var problems = new List<string>();
        if (settings.IsEnrolled)
        {
            var owner = Path.Combine(PeekShieldSettings.PluginDir, "enrollment", "embeddings.bin");
            if (!File.Exists(owner))
                problems.Add("机主人脸数据文件缺失（enrollment/embeddings.bin）");
        }
        if (settings.WhitelistEnabled && settings.Whitelist.Count == 0)
            problems.Add("已开启白名单但未添加任何成员");
        if (problems.Count == 0)
            return new DiagnosticItem { Name = "人脸数据完整性", Status = DiagnosticStatus.Ok, Detail = "已录入的人脸数据文件均完整。" };
        return new DiagnosticItem { Name = "人脸数据完整性", Status = DiagnosticStatus.Warn, Detail = string.Join("；", problems) };
    }

    private static DiagnosticItem CheckAutostart(PeekShieldSettings settings)
    {
        if (!settings.AutoStartWithClassIsland)
            return new DiagnosticItem { Name = "随宿主启动", Status = DiagnosticStatus.Ok, Detail = "未开启随 ClassIsland 启动，跳过。" };
        return new DiagnosticItem { Name = "随宿主启动", Status = DiagnosticStatus.Ok, Detail = "已开启随 ClassIsland 启动（由宿主管理自启）。" };
    }

    private static DiagnosticItem CheckSettingsConsistency(PeekShieldSettings settings)
    {
        var warns = new List<string>();
        if (settings.WhitelistEnabled && settings.Whitelist.Count == 0)
            warns.Add("白名单已开启但没有成员，等同于未启用白名单");
        if (settings.AutoBackupEnabled && string.IsNullOrWhiteSpace(settings.AutoBackupDir))
            warns.Add("已开启自动备份但未指定备份目录，将使用默认目录（logs/backups）");
        if (settings.SilentCaptureStrangers && string.IsNullOrWhiteSpace(settings.SilentCaptureDir))
            warns.Add("已开启静默取证但未指定目录，将使用默认目录（logs/evidence）");
        if (warns.Count > 0)
            return new DiagnosticItem { Name = "设置有效性", Status = DiagnosticStatus.Warn, Detail = string.Join("；", warns) };
        return new DiagnosticItem { Name = "设置有效性", Status = DiagnosticStatus.Ok, Detail = "配置一致，无冲突。" };
    }

    private static DiagnosticItem CheckDataDir()
    {
        try
        {
            Directory.CreateDirectory(PeekShieldSettings.PluginDir);
            var tmp = Path.Combine(PeekShieldSettings.PluginDir, ".diag_writable_test");
            File.WriteAllText(tmp, "1");
            File.Delete(tmp);
            return new DiagnosticItem { Name = "数据目录", Status = DiagnosticStatus.Ok, Detail = $"数据目录可写（{PeekShieldSettings.PluginDir}）" };
        }
        catch (Exception ex)
        {
            return new DiagnosticItem { Name = "数据目录", Status = DiagnosticStatus.Error, Detail = "数据目录不可写：" + ex.Message };
        }
    }

    private static DiagnosticItem CheckConfigBackup()
    {
        int count = 0;
        try
        {
            var dir = Path.Combine(PeekShieldSettings.PluginDir, "logs", "backups");
            if (Directory.Exists(dir)) count = Directory.GetFiles(dir, "*.kyd").Length;
        }
        catch { }
        string detail = count > 0 ? $"已存在 {count} 个 .kyd 备份文件（可用于多配置切换 / 迁移）。" : "尚未创建 .kyd 备份（可在「备份与恢复」分区创建）。";
        return new DiagnosticItem { Name = "配置备份", Status = DiagnosticStatus.Ok, Detail = detail };
    }
}
