namespace eduHub.api.Options;

public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    public string? ApiKey { get; set; }
    public string FromEmail { get; set; } = "EduHub <onboarding@resend.dev>";
}
