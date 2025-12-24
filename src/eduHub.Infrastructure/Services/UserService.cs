using eduHub.Application.DTOs.Users;
using eduHub.Application.Interfaces.Users;
using eduHub.Application.Security;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;


namespace eduHub.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _context;
    private readonly JwtOptions _jwtOptions;
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword("eduHub-dummy-password");

    public UserService(AppDbContext context, IOptions<JwtOptions> jwtOptions)
    {
        _context = context;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<UserResponseDto> RegisterAsync(UserRegisterDto dto)
    {

        var exists = await _context.Users
            .AnyAsync(u => u.UserName == dto.UserName || u.Email == dto.Email);

        if (exists)
            throw new InvalidOperationException("Unable to register.");

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        var user = new User
        {
            UserName = dto.UserName,
            Email = dto.Email,
            PasswordHash = passwordHash,
            Role = UserRole.User,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Users.Add(user);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Check for Postgres unique constraint violation
            if (ex.InnerException is PostgresException { SqlState: "23505" })
                throw new InvalidOperationException("Unable to register.");

            // Check for Sqlite unique constraint violation (Error code 19)
            if (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
                throw new InvalidOperationException("Unable to register.");

            throw;
        }

        return new UserResponseDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            Role = user.Role.ToString()
        };
    }

    public async Task<AuthResponseDto?> LoginAsync(UserLoginDto dto)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u =>
                u.UserName == dto.UserNameOrEmail ||
                u.Email == dto.UserNameOrEmail);

        if (user == null)
        {
            _ = BCrypt.Net.BCrypt.Verify(dto.Password, DummyPasswordHash);
            return null;
        }

        var valid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
        if (!valid)
            return null;

        var accessToken = GenerateJwtToken(user, out var expiresAtUtc, out _);  

        var now = DateTimeOffset.UtcNow;
        var refreshDays = _jwtOptions.RefreshTokenDays;

        var refreshToken = GenerateSecureToken();
        var refreshTokenHash = HashToken(refreshToken);
        var refreshExpiresAtUtc = now.AddDays(refreshDays);

        // Fetch all tokens for user and filter in memory to avoid EF Core translation issues with DateTimeOffset on Sqlite
        var allUserTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == user.Id)
            .ToListAsync();

        var staleTokens = allUserTokens
            .Where(rt => rt.ExpiresAtUtc <= now || rt.RevokedAtUtc != null)
            .ToList();
        if (staleTokens.Count > 0)
            _context.RefreshTokens.RemoveRange(staleTokens);

        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpiresAtUtc
        });

        await _context.SaveChangesAsync();

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            ExpiresAtUtc = expiresAtUtc,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAtUtc = refreshExpiresAtUtc,
            User = new UserResponseDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                Role = user.Role.ToString()
            }
        };
    }

    public async Task<AuthResponseDto?> RefreshAsync(RefreshRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken))
            return null;

        var now = DateTimeOffset.UtcNow;
        var refreshHash = HashToken(dto.RefreshToken);

        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == refreshHash);

        if (storedToken == null)
            return null;

        if (storedToken.RevokedAtUtc != null)
        {
            await RevokeRefreshTokensAsync(storedToken.UserId, now);
            return null;
        }

        if (storedToken.ExpiresAtUtc <= now)
            return null;

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == storedToken.UserId);
        if (user == null)
            return null;

        storedToken.RevokedAtUtc = now;

        var accessToken = GenerateJwtToken(user, out var accessExpiresAtUtc, out _);

        var refreshDays = _jwtOptions.RefreshTokenDays;

        var newRefreshToken = GenerateSecureToken();
        var newRefreshHash = HashToken(newRefreshToken);
        var refreshExpiresAtUtc = now.AddDays(refreshDays);

        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newRefreshHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpiresAtUtc
        });

        await _context.SaveChangesAsync();

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            ExpiresAtUtc = accessExpiresAtUtc,
            RefreshToken = newRefreshToken,
            RefreshTokenExpiresAtUtc = refreshExpiresAtUtc,
            User = new UserResponseDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                Role = user.Role.ToString()
            }
        };
    }

    public async Task RevokeTokenAsync(string jti, DateTimeOffset expiresAtUtc, int userId)
    {
        if (string.IsNullOrWhiteSpace(jti))
            return;

        var alreadyRevoked = await _context.RevokedTokens.AnyAsync(t => t.Jti == jti);
        if (alreadyRevoked)
            return;

        _context.RevokedTokens.Add(new RevokedToken
        {
            Jti = jti,
            UserId = userId,
            ExpiresAtUtc = expiresAtUtc,
            RevokedAtUtc = DateTimeOffset.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    public Task RevokeRefreshTokensAsync(int userId)
    {
        return RevokeRefreshTokensAsync(userId, DateTimeOffset.UtcNow);
    }

    private async Task RevokeRefreshTokensAsync(int userId, DateTimeOffset revokedAtUtc)
    {
        var tokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAtUtc == null)
            .ToListAsync();

        if (tokens.Count == 0)
            return;

        foreach (var token in tokens)
            token.RevokedAtUtc = revokedAtUtc;

        await _context.SaveChangesAsync();
    }

    private static string GenerateSecureToken(int byteLength = 64)
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

    private string GenerateJwtToken(User user, out DateTimeOffset expiresAtUtc, out string jti)
    {
        var key = _jwtOptions.Key;
        var issuer = _jwtOptions.Issuer;
        var audience = _jwtOptions.Audience;

        jti = Guid.NewGuid().ToString("N");
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var minutes = _jwtOptions.AccessTokenMinutes;
        var now = DateTimeOffset.UtcNow;
        expiresAtUtc = now.AddMinutes(minutes);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
