using System;
using System.Linq;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using eduHub.Application.DTOs.Reservations;
using eduHub.Application.Interfaces.Reservations;
using eduHub.Application.Interfaces.Tenants;
using eduHub.Application.Options;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/org/public/reservations")]
[AllowAnonymous]
public class PublicReservationsController : ApiControllerBase
{
    private static readonly TimeSpan VerificationTtl = TimeSpan.FromMinutes(10);
    private readonly AppDbContext _context;
    private readonly IReservationService _reservationService;
    private readonly ICurrentTenant _tenant;
    private readonly ReservationPolicyOptions _policy;
    private readonly IHostEnvironment _environment;

    public PublicReservationsController(
        AppDbContext context,
        IReservationService reservationService,
        ICurrentTenant tenant,
        IOptions<ReservationPolicyOptions> policyOptions,
        IHostEnvironment environment)
    {
        _context = context;
        _reservationService = reservationService;
        _tenant = tenant;
        _policy = policyOptions.Value;
        _environment = environment;
    }

    [HttpPost("guest/verify")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(GuestReservationVerifyResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GuestReservationVerifyResponseDto>> RequestVerification(
        [FromBody] GuestReservationVerifyRequestDto dto)
    {
        if (!_tenant.OrganizationId.HasValue)
            return NotFoundProblem("Tenant not found.");

        var email = dto.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequestProblem("Email is required.", "EmailRequired");

        if (!IsValidEmail(email))
            return BadRequestProblem("Email is invalid.", "EmailInvalid");

        var normalizedEmail = NormalizeEmail(email);
        var now = DateTimeOffset.UtcNow;
        var token = GenerateToken();
        var tokenHash = HashToken(token);

        await CleanupExpiredTokensAsync(now);

        await _context.GuestReservationTokens
            .Where(t => t.Email == normalizedEmail)
            .ExecuteDeleteAsync();

        _context.GuestReservationTokens.Add(new GuestReservationToken
        {
            Id = Guid.NewGuid(),
            OrganizationId = _tenant.OrganizationId.Value,
            Email = normalizedEmail,
            TokenHash = tokenHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(VerificationTtl),
            CreatedFromIp = HttpContext.Connection.RemoteIpAddress?.ToString()
        });
        await _context.SaveChangesAsync();

        await ApplyJitterAsync();

        return Ok(new GuestReservationVerifyResponseDto
        {
            VerificationSent = true,
            DebugToken = _environment.IsDevelopment() ? token : null
        });
    }

    [HttpPost("guest/verify/confirm")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(GuestReservationVerifyConfirmResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GuestReservationVerifyConfirmResponseDto>> ConfirmVerification(
        [FromBody] GuestReservationVerifyConfirmRequestDto dto)
    {
        if (!_tenant.OrganizationId.HasValue)
            return NotFoundProblem("Tenant not found.");

        var token = dto.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return BadRequestProblem("Verification token is required.", "TokenRequired");

        var tokenHash = HashToken(token);
        var now = DateTimeOffset.UtcNow;
        await CleanupExpiredTokensAsync(now);

        var entry = await _context.GuestReservationTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
        if (entry == null)
        {
            await ApplyJitterAsync();
            return Ok(new GuestReservationVerifyConfirmResponseDto());
        }

        if (entry.ExpiresAtUtc <= now)
        {
            _context.GuestReservationTokens.Remove(entry);
            await _context.SaveChangesAsync();
            await ApplyJitterAsync();
            return Ok(new GuestReservationVerifyConfirmResponseDto());
        }

        entry.VerifiedAtUtc ??= now;
        entry.ExpiresAtUtc = now.AddHours(_policy.GuestAccessHours);
        await _context.SaveChangesAsync();

        await ApplyJitterAsync();

        return Ok(new GuestReservationVerifyConfirmResponseDto
        {
            Verified = true,
            GuestToken = token
        });
    }

    [HttpGet("guest/pending-count")]
    [ProducesResponseType(typeof(ReservationPendingCountResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ReservationPendingCountResponseDto>> GetGuestPendingCount(
        [FromQuery] string guestToken)
    {
        var email = await ResolveGuestEmailAsync(guestToken);
        if (email == null)
            return BadRequestProblem("Guest verification is required.", "GuestTokenInvalid");

        var now = DateTimeOffset.UtcNow;
        var count = await _context.Reservations
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Pending)
            .Where(r => r.GuestEmail == email)
            .Where(r => r.ExpiresAtUtc == null || r.ExpiresAtUtc > now)
            .CountAsync();

        return Ok(new ReservationPendingCountResponseDto { PendingCount = count });
    }

    [HttpGet("policy")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ReservationPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public ActionResult<ReservationPolicyDto> GetPolicy()
    {
        if (!_tenant.OrganizationId.HasValue)
            return NotFoundProblem("Tenant not found.");

        return Ok(new ReservationPolicyDto
        {
            LeadTimeMinutes = _policy.LeadTimeMinutes,
            MaxAdvanceDays = _policy.MaxAdvanceDays,
            SlotMinutes = _policy.SlotMinutes,
            MaxDurationMinutes = _policy.MaxDurationMinutes,
            BufferMinutes = _policy.BufferMinutes,
            PendingExpiryHours = _policy.PendingExpiryHours,
            MaxPendingPerUser = _policy.MaxPendingPerUser,
            GuestAccessHours = _policy.GuestAccessHours
        });
    }

    [HttpGet("pending-count")]
    [ProducesResponseType(typeof(ReservationPendingCountResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ReservationPendingCountResponseDto>> GetSlotPendingCount(
        [FromQuery] int roomId,
        [FromQuery] DateTimeOffset startTimeUtc,
        [FromQuery] DateTimeOffset endTimeUtc)
    {
        if (roomId <= 0)
            return BadRequestProblem("roomId must be greater than 0.");

        if (startTimeUtc >= endTimeUtc)
            return BadRequestProblem("startTimeUtc must be earlier than endTimeUtc.");

        var now = DateTimeOffset.UtcNow;
        var startUtc = startTimeUtc.ToUniversalTime();
        var endUtc = endTimeUtc.ToUniversalTime();
        var count = await _context.Reservations
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Pending)
            .Where(r => r.RoomId == roomId)
            .Where(r => r.ExpiresAtUtc == null || r.ExpiresAtUtc > now)
            .Where(r => r.StartTimeUtc < endUtc && r.EndTimeUtc > startUtc)
            .CountAsync();

        return Ok(new ReservationPendingCountResponseDto { PendingCount = count });
    }

    [HttpPost("guest")]
    [ProducesResponseType(typeof(ReservationResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ReservationResponseDto>> CreateGuestReservation(
        [FromBody] GuestReservationCreateDto dto)
    {
        var email = await ResolveGuestEmailAsync(dto.GuestToken);
        if (email == null)
            return BadRequestProblem("Guest verification is required.", "GuestTokenInvalid");

        var reservation = await _reservationService.CreateGuestAsync(
            new ReservationCreateDto
            {
                RoomId = dto.RoomId,
                StartTimeUtc = dto.StartTimeUtc,
                EndTimeUtc = dto.EndTimeUtc,
                Purpose = dto.Purpose
            },
            email);

        return StatusCode(StatusCodes.Status201Created, reservation);
    }

    private async Task<string?> ResolveGuestEmailAsync(string guestToken)
    {
        if (!_tenant.OrganizationId.HasValue)
            return null;

        var token = guestToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var tokenHash = HashToken(token);
        var now = DateTimeOffset.UtcNow;
        await CleanupExpiredTokensAsync(now);

        var entry = await _context.GuestReservationTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
        if (entry == null)
        {
            await ApplyJitterAsync();
            return null;
        }

        if (entry.ExpiresAtUtc <= now)
        {
            _context.GuestReservationTokens.Remove(entry);
            await _context.SaveChangesAsync();
            await ApplyJitterAsync();
            return null;
        }

        return entry.Email;
    }

    private Task CleanupExpiredTokensAsync(DateTimeOffset now)
    {
        return _context.GuestReservationTokens
            .Where(t => t.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync();
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

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}
