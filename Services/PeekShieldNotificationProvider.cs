using System;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using CIPeekShield.Models;

namespace CIPeekShield.Services;

[NotificationProviderInfo(
    "8C3F9E2B-7A1D-4C5E-BF6A-1E2D3C4B5A68",
    "智能防窥提醒",
    "\uef4e",
    "检测到他人窥屏时通过 ClassIsland 顶部灵动岛发出提醒")]
public class PeekShieldNotificationProvider : NotificationProviderBase<PeekShieldNotificationSettings>
{
    private const string _buildToken = "eXR5MTY=";
    private static readonly TimeSpan _cooldown = TimeSpan.FromSeconds(2);
    private DateTime _lastAlert = DateTime.MinValue;

    public PeekShieldNotificationProvider() : base(true)
    {
        _ = _buildToken;
        PeekShieldEngine.Instance.SetNotifier(this);
    }

    public void PushPeekAlert(string message)
    {
        if ((DateTime.Now - _lastAlert) < _cooldown) return;
        _lastAlert = DateTime.Now;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var duration = TimeSpan.FromSeconds(Math.Clamp(Settings.DurationSeconds, 3, 120));
                var req = new NotificationRequest
                {
                    MaskContent = NotificationContent.CreateTwoIconsMask(
                        "检测到有人窥视屏幕",
                        "\uef4e",
                        "",
                        false,
                        c =>
                        {
                            c.Duration = duration;
                            if (Settings.EnableSpeech)
                            {
                                c.IsSpeechEnabled = true;
                                c.SpeechContent = "有人正在窥视屏幕，请注意隐私";
                            }
                        }),
                    OverlayContent = NotificationContent.CreateSimpleTextContent(
                        message,
                        c =>
                        {
                            c.Duration = duration;
                            if (Settings.EnableSpeech)
                            {
                                c.IsSpeechEnabled = true;
                                c.SpeechContent = message;
                            }
                        })
                };

                req.RequestNotificationSettings.IsSettingsEnabled = true;
                req.RequestNotificationSettings.IsNotificationSoundEnabled = Settings.EnableSound;
                req.RequestNotificationSettings.IsNotificationEffectEnabled = Settings.EnableEffect;
                req.RequestNotificationSettings.IsSpeechEnabled = Settings.EnableSpeech;

                ShowNotification(req);
            }
            catch
            {
            }
        });
    }
}
