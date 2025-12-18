using eduHub.Application.DTOs.Users;
using eduHub.Application.Interfaces.Users;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private readonly IConfiguration _configuration;
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword("eduHub-dummy-password");

    public UserService(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new InvalidOperationException("Unable to register.");
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
        var refreshDays = _configuration.GetValue("Jwt:RefreshTokenDays", 30);
        if (refreshDays < 1 || refreshDays > 90)
            throw new InvalidOperationException("Jwt:RefreshTokenDays must be between 1 and 90.");

        var refreshToken = GenerateSecureToken();
        var refreshTokenHash = HashToken(refreshToken);
        var refreshExpiresAtUtc = now.AddDays(refreshDays);

        var staleTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && (rt.ExpiresAtUtc <= now || rt.RevokedAtUtc != null))
            .ToListAsync();
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

        if (storedToken == null || storedToken.RevokedAtUtc != null || storedToken.ExpiresAtUtc <= now)
            return null;

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == storedToken.UserId);
        if (user == null)
            return null;

        storedToken.RevokedAtUtc = now;

        var accessToken = GenerateJwtToken(user, out var accessExpiresAtUtc, out _);

        var refreshDays = _configuration.GetValue("Jwt:RefreshTokenDays", 30);
        if (refreshDays < 1 || refreshDays > 90)
            throw new InvalidOperationException("Jwt:RefreshTokenDays must be between 1 and 90.");

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
        var jwtSection = _configuration.GetSection("Jwt");
        var key = jwtSection["Key"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Jwt:Key is missing.");
        if (Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Jwt:Key must be at least 32 bytes (256-bit).");

        var issuer = jwtSection["Issuer"];
        if (string.IsNullOrWhiteSpace(issuer))
            issuer = "eduHub";

        var audience = jwtSection["Audience"];
        if (string.IsNullOrWhiteSpace(audience))
            audience = "eduHub";

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

        var minutes = _configuration.GetValue("Jwt:AccessTokenMinutes", 15);
        if (minutes < 5 || minutes > 60)
            throw new InvalidOperationException("Jwt:AccessTokenMinutes must be between 5 and 60.");
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
