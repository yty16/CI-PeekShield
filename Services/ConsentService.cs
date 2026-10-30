using CIPeekShield.Models;

namespace CIPeekShield.Services;

// 隐私同意门控：人脸处理属于敏感个人信息，必须先取得用户明确同意（含单独的人脸处理同意），
// 且同意版本需与当前策略版本一致，否则不启动任何人脸检测 / 识别处理。
public static class ConsentService
{
    public static bool NeedsConsent(PeekShieldSettings s)
        => !s.ConsentPrivacyPolicy || s.ConsentVersion < PeekShieldSettings.CurrentConsentVersion;

    // 是否允许进行人脸处理（检测 / 识别）
    public static bool CanProcessFace(PeekShieldSettings s)
        => s.ConsentPrivacyPolicy
           && s.ConsentFaceProcessing
           && s.ConsentVersion >= PeekShieldSettings.CurrentConsentVersion;

    // 在设置页由用户主动确认同意
    public static void SetConsent(PeekShieldSettings s, bool privacyAccepted, bool faceProcessingAccepted)
    {
        s.ConsentPrivacyPolicy = privacyAccepted;
        s.ConsentFaceProcessing = faceProcessingAccepted;
        s.ConsentVersion = PeekShieldSettings.CurrentConsentVersion;
        s.ConsentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        s.Save();
        LoggerService.LogInfo($"隐私告知已确认：政策={privacyAccepted} 人脸处理单独同意={faceProcessingAccepted} 版本={s.ConsentVersion}");
    }

    public static void Revoke(PeekShieldSettings s)
    {
        s.ConsentPrivacyPolicy = false;
        s.ConsentFaceProcessing = false;
        s.ConsentVersion = 0;
        s.ConsentTime = "";
        s.Save();
        LoggerService.LogInfo("用户已撤回同意，人脸侦测停用，下次启动将重新征求同意");
    }
}
