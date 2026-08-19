namespace CIPeekShield.Models;

public class PeekShieldNotificationSettings
{
    public int DurationSeconds { get; set; } = 10;

    public bool EnableEffect { get; set; } = true;

    public bool EnableSpeech { get; set; } = false;

    public bool EnableSound { get; set; } = true;

    public int StrangerAlertLimit { get; set; } = 2;

    public int StrangerAlertCooldownMinutes { get; set; } = 10;
}
