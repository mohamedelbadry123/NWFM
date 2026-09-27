namespace Auth.Domain.Options;

public sealed class TeamOtpOptions
{
    public const string SectionName = "TeamOtp";

    public bool Enabled { get; set; } = true;
    public int CodeLength { get; set; } = 6;
    public int ExpiryMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 5;
    public int ResendCooldownSeconds { get; set; } = 60;
    public string MessageTemplateAr { get; set; } = "رمز الدخول الخاص بك هو {0}";
    public string MessageTemplateEn { get; set; } = "Your login verification code is {0}";
}
