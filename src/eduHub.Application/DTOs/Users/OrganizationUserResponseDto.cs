using System;

namespace eduHub.Application.DTOs.Users;

public class OrganizationUserResponseDto
{
    public int Id { get; set; }
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset JoinedAtUtc { get; set; }
    public bool IsPlatformAdmin { get; set; }
}
