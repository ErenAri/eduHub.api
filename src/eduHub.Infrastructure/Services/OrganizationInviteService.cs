using eduHub.Application.DTOs.Organizations;
using eduHub.Application.Interfaces.Organizations;
using eduHub.Domain.Entities;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace eduHub.Infrastructure.Services;

public class OrganizationInviteService : IOrganizationInviteService
{
    private readonly AppDbContext _context;

    public OrganizationInviteService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<(OrganizationInvite Invite, string Token)> CreateAsync(
        Guid organizationId,
        OrganizationInviteCreateDto dto,
        int createdByUserId)
    {
        var orgExists = await _context.Organizations
            .AnyAsync(o => o.Id == organizationId && o.IsActive);
        if (!orgExists)
            throw new InvalidOperationException("Organization is not active.");

        var now = DateTimeOffset.UtcNow;
        var email = dto.Email.Trim();
        var token = GenerateSecureToken();
        var tokenHash = HashToken(token);
        var expiresAtUtc = now.AddDays(dto.ExpiresInDays ?? 7);

        var invite = new OrganizationInvite
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = email,
            Role = dto.Role,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = now,
            CreatedByUserId = createdByUserId
        };

        _context.OrganizationInvites.Add(invite);
        _context.AuditLogs.Add(new AuditLog
        {
            Action = "InviteCreated",
            EntityType = "OrganizationInvite",
            EntityId = invite.Id.ToString(),
            Summary = invite.Email,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = now
        });

        await _context.SaveChangesAsync();
        return (invite, token);
    }

    public async Task<OrganizationInvite?> GetByIdAsync(Guid inviteId)
    {
        return await _context.OrganizationInvites
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == inviteId);
    }

    public async Task RevokeAsync(Guid inviteId, int revokedByUserId)
    {
        var invite = await _context.OrganizationInvites.FirstOrDefaultAsync(i => i.Id == inviteId);
        if (invite == null)
            throw new KeyNotFoundException("Invite not found.");

        if (invite.RevokedAtUtc != null)
            return;

        invite.RevokedAtUtc = DateTimeOffset.UtcNow;
        _context.AuditLogs.Add(new AuditLog
        {
            Action = "InviteRevoked",
            EntityType = "OrganizationInvite",
            EntityId = invite.Id.ToString(),
            Summary = invite.Email,
            CreatedByUserId = revokedByUserId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    private static string GenerateSecureToken(int byteLength = 48)
    {
        Span<byte> buffer = byteLength <= 128 ? stackalloc byte[byteLength] : new byte[byteLength];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer);
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}
