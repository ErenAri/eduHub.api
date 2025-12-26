using System;
using eduHub.Domain.Enums;

namespace eduHub.Application.DTOs.Organizations;

public class OrganizationInviteResponseDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public OrganizationMemberRole Role { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string? Token { get; set; }
}
