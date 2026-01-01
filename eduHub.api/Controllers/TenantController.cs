using System;
using System.Linq;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using eduHub.Application.DTOs.Tenants;
using eduHub.Domain.Enums;
using eduHub.Domain.Entities;
using eduHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/tenant")]
public class TenantController : ApiControllerBase
{
    private static readonly TimeSpan VerificationTtl = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _context;
    private readonly IHostEnvironment _environment;

    public TenantController(
        AppDbContext context,
        IHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    [HttpPost("resolve")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(TenantResolveRequestResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TenantResolveRequestResponseDto>> Resolve(
        [FromBody] TenantResolveRequestDto dto)
    {
        var email = dto.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequestProblem("Email is required.", "EmailRequired");

        if (!IsValidEmail(email))
            return BadRequestProblem("Email is invalid.", "EmailInvalid");

        var normalizedEmail = email.ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var token = GenerateToken();
        var tokenHash = HashToken(token);

        await CleanupExpiredTokensAsync(now);

        _context.TenantResolveTokens.Add(new TenantResolveToken
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            TokenHash = tokenHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(VerificationTtl),
            CreatedFromIp = HttpContext.Connection.RemoteIpAddress?.ToString()
        });
        await _context.SaveChangesAsync();

        await ApplyJitterAsync();

        var response = new TenantResolveRequestResponseDto
        {
            VerificationSent = true,
            DebugToken = _environment.IsDevelopment() ? token : null
        };

        return Ok(response);
    }

    [HttpPost("resolve/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(TenantResolveResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TenantResolveResponseDto>> Verify(
        [FromBody] TenantResolveVerifyRequestDto dto)
    {
        var token = dto.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return BadRequestProblem("Verification token is required.", "TokenRequired");

        var tokenHash = HashToken(token);

        var now = DateTimeOffset.UtcNow;
        await CleanupExpiredTokensAsync(now);

        var entry = await _context.TenantResolveTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
        if (entry == null)
        {
            await ApplyJitterAsync();
            return Ok(new TenantResolveResponseDto());
        }

        if (entry.UsedAtUtc.HasValue || entry.ExpiresAtUtc <= now)
        {
            _context.TenantResolveTokens.Remove(entry);
            await _context.SaveChangesAsync();
            await ApplyJitterAsync();
            return Ok(new TenantResolveResponseDto());
        }

        entry.UsedAtUtc = now;
        await _context.SaveChangesAsync();

        var tenants = await GetTenantsForEmailAsync(entry.Email);
        await ApplyJitterAsync();
        return Ok(new TenantResolveResponseDto { Tenants = tenants });
    }

    private async Task<List<TenantSummaryDto>> GetTenantsForEmailAsync(string normalizedEmail)
    {
        var now = DateTimeOffset.UtcNow;

        var memberOrgIds = _context.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.Status == OrganizationMemberStatus.Active)
            .Join(
                _context.Users.AsNoTracking(),
                member => member.UserId,
                user => user.Id,
                (member, user) => new { member.OrganizationId, user.Email })
            .Where(entry => entry.Email.ToLower() == normalizedEmail)
            .Select(entry => entry.OrganizationId);

        var inviteOrgIds = _context.OrganizationInvites
            .AsNoTracking()
            .Where(invite => invite.Email.ToLower() == normalizedEmail)
            .Where(invite => invite.UsedAtUtc == null &&
                             invite.RevokedAtUtc == null &&
                             invite.ExpiresAtUtc > now)
            .Select(invite => invite.OrganizationId);

        var orgIds = memberOrgIds.Union(inviteOrgIds);

        return await _context.Organizations
            .AsNoTracking()
            .Where(org => org.IsActive)
            .Where(org => orgIds.Contains(org.Id))
            .OrderBy(org => org.Name)
            .Select(org => new TenantSummaryDto
            {
                Id = org.Id,
                Name = org.Name,
                Slug = org.Slug,
                LogoUrl = org.LogoUrl
            })
            .ToListAsync();
    }

    private static async Task ApplyJitterAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(150, 301)));
    }

    private static string GenerateToken(int byteLength = 32)
    {
        Span<byte> buffer = byteLength <= 128 ? stackalloc byte[byteLength] : new byte[byteLength];
        RandomNumberGenerator.Fill(buffer);
        return Base64UrlEncode(buffer);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        var base64 = Convert.ToBase64String(bytes);
        return base64.Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }

    private Task CleanupExpiredTokensAsync(DateTimeOffset now)
    {
        return _context.TenantResolveTokens
            .Where(t => t.ExpiresAtUtc <= now || t.UsedAtUtc != null)
            .ExecuteDeleteAsync();
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var parsed = new MailAddress(email);
            return string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

}
