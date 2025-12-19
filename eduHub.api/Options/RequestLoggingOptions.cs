namespace eduHub.api.Options;

public sealed class RequestLoggingOptions
{
    public const string SectionName = "RequestLogging";

    public bool Enabled { get; set; } = true;
    public int SlowRequestThresholdMs { get; set; } = 2000;
}
