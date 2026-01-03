using eduHub.Application.DTOs.Public;

namespace eduHub.api.Services;

public interface IContactEmailSender
{
    Task<ContactEmailResult> SendAsync(PublicContactRequestDto dto, CancellationToken cancellationToken);
}

public sealed record ContactEmailResult(bool Sent, bool Skipped);
