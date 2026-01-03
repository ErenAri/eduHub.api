using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using eduHub.Application.DTOs.Public;
using eduHub.api.Options;
using Microsoft.Extensions.Options;

namespace eduHub.api.Services;

public sealed class ResendContactEmailSender : IContactEmailSender
{
    private const string ResendEndpoint = "https://api.resend.com/emails";
    private const string SupportEmail = "support@eduhub.website";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ResendContactEmailSender> _logger;
    private readonly ResendOptions _options;

    public ResendContactEmailSender(
        IHttpClientFactory httpClientFactory,
        IOptions<ResendOptions> options,
        ILogger<ResendContactEmailSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<ContactEmailResult> SendAsync(
        PublicContactRequestDto dto,
        CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("Resend API key is not configured. Contact email sending is disabled.");
            return new ContactEmailResult(false, true);
        }

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, ResendEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            from = string.IsNullOrWhiteSpace(_options.FromEmail)
                ? "EduHub <onboarding@resend.dev>"
                : _options.FromEmail,
            to = new[] { SupportEmail },
            subject = "EduHub public contact request",
            text = BuildMessage(dto)
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Resend email send failed with status {StatusCode}.", response.StatusCode);
            return new ContactEmailResult(false, false);
        }

        _logger.LogInformation("Resend email sent for public contact request.");
        return new ContactEmailResult(true, false);
    }

    private string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            return _options.ApiKey;

        return Environment.GetEnvironmentVariable("RESEND_API_KEY");
    }

    private static string BuildMessage(PublicContactRequestDto dto)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Name: {dto.Name}");
        builder.AppendLine($"Email: {dto.Email}");
        builder.AppendLine($"Organization: {dto.Organization}");
        builder.AppendLine();
        builder.Append(dto.Message);
        return builder.ToString();
    }
}
