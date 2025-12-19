namespace eduHub.api.Options;

public sealed class TokenCleanupOptions
{
    public const string SectionName = "TokenCleanup";

    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
}
