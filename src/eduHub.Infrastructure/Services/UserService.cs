using eduHub.Application.DTOs.Organizations;
using eduHub.Application.DTOs.Users;
using eduHub.Application.Interfaces.Users;
using eduHub.Application.Common.Exceptions;
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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new InvalidOperationException("Unable to register.");
        }

        return MapUser(user);
    }

    public async Task<AuthResponseDto> RegisterInOrganizationAsync(UserRegisterDto dto, Guid organizationId)
    {
        var userName = dto.UserName?.Trim();
        var email = dto.Email?.Trim();

        if (string.IsNullOrWhiteSpace(userName))
            throw new InvalidOperationException("Username is required.");

        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Email is required.");

        if (string.IsNullOrWhiteSpace(dto.Password))
            throw new InvalidOperationException("Password is required.");

        if (!await IsOrganizationActiveAsync(organizationId))
            throw new InvalidOperationException("Organization is not active.");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.UserName == userName || u.Email == email);

        if (user == null)
        {
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            user = new User
            {
                UserName = userName,
                Email = email,
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
        }
        else
        {
            var valid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            if (!valid)
                throw new InvalidOperationException("Unable to register.");
        }

        var existingMembership = await _context.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == user.Id);

        if (existingMembership != null)
            throw new InvalidOperationException("User is already a member of this organization.");

        _context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId = user.Id,
            Role = OrganizationMemberRole.User,
            Status = OrganizationMemberStatus.Active,
            JoinedAtUtc = DateTimeOffset.UtcNow
        });

        await _context.SaveChangesAsync();

        return await IssueTokensAsync(user, organizationId, OrganizationMemberRole.User);
    }

    public async Task<AuthResponseDto?> LoginPlatformAsync(UserLoginDto dto)
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

        if (user.Role != UserRole.Admin)
            return null;

        return await IssuePlatformTokensAsync(user);
    }

    public async Task<AuthResponseDto?> LoginAsync(UserLoginDto dto, Guid organizationId)
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

        if (!await IsOrganizationActiveAsync(organizationId))
            return null;

        var membership = await GetActiveMembershipAsync(user.Id, organizationId);
        if (membership == null)
            return null;

        return await IssueTokensAsync(user, organizationId, membership.Role);
    }

    public async Task<AuthResponseDto?> RefreshPlatformAsync(RefreshRequestDto dto)
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
        if (user == null || user.Role != UserRole.Admin)
            return null;

        storedToken.RevokedAtUtc = now;

        return await IssuePlatformTokensAsync(user, now);
    }

    public async Task<AuthResponseDto?> RefreshAsync(RefreshRequestDto dto, Guid organizationId)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken))
            return null;

        if (!await IsOrganizationActiveAsync(organizationId))
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

        var membership = await GetActiveMembershipAsync(user.Id, organizationId);
        if (membership == null)
            return null;

        storedToken.RevokedAtUtc = now;

        return await IssueTokensAsync(user, organizationId, membership.Role, now);
    }

    public async Task<AuthResponseDto> RedeemInviteAsync(OrganizationInviteRedeemDto dto, Guid organizationId)
    {
        var token = dto.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Invite token is required.");

        if (string.IsNullOrWhiteSpace(dto.Password))
            throw new InvalidOperationException("Password is required.");

        if (!await IsOrganizationActiveAsync(organizationId))
            throw new InvalidOperationException("Organization is not active.");

        var tokenHash = HashToken(token);
        var invite = await _context.OrganizationInvites
            .FirstOrDefaultAsync(i => i.OrganizationId == organizationId && i.TokenHash == tokenHash);

        if (invite == null)
            throw new InvalidOperationException("Invite not found.");

        if (invite.RevokedAtUtc != null)
            throw new InvalidOperationException("Invite has been revoked.");

        if (invite.UsedAtUtc != null)
            throw new InvalidOperationException("Invite has already been used.");

        if (invite.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Invite has expired.");

        var email = invite.Email.Trim();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            var userName = string.IsNullOrWhiteSpace(dto.UserName) ? email : dto.UserName.Trim();
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            user = new User
            {
                UserName = userName,
                Email = email,
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
                throw new InvalidOperationException("Unable to create user.");
            }
        }

        var existingMembership = await _context.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == user.Id);

        if (existingMembership != null && existingMembership.Status == OrganizationMemberStatus.Active)
            throw new InvalidOperationException("User is already a member of this organization.");

        if (existingMembership == null)
        {
            _context.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = organizationId,
                UserId = user.Id,
                Role = invite.Role,
                Status = OrganizationMemberStatus.Active,
                JoinedAtUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existingMembership.Role = invite.Role;
            existingMembership.Status = OrganizationMemberStatus.Active;
            existingMembership.JoinedAtUtc = DateTimeOffset.UtcNow;
        }

        invite.UsedAtUtc = DateTimeOffset.UtcNow;
        invite.UsedByUserId = user.Id;

        await _context.SaveChangesAsync();

        return await IssueTokensAsync(user, organizationId, invite.Role);
    }

    public async Task<UserResponseDto?> GetByIdAsync(int userId)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return null;

        return MapUser(user);
    }

    public async Task<UserResponseDto?> GetByIdInOrgAsync(int userId, Guid organizationId)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return null;

        var membership = await GetActiveMembershipAsync(userId, organizationId);
        if (membership == null)
            return null;

        return MapUser(user, membership.Role);
    }

    public async Task<IReadOnlyList<OrganizationUserResponseDto>> GetOrganizationUsersAsync(Guid organizationId)
    {
        var members = await _context.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .Join(
                _context.Users.AsNoTracking(),
                member => member.UserId,
                user => user.Id,
                (member, user) => new { member, user })
            .OrderBy(row => row.user.UserName)
            .ToListAsync();

        return members
            .Select(row => MapOrganizationUser(row.user, row.member))
            .ToList();
    }

    public async Task<OrganizationUserResponseDto> CreateOrganizationUserAsync(
        Guid organizationId,
        OrganizationUserCreateDto dto)
    {
        var userName = dto.UserName.Trim();
        var email = dto.Email.Trim();

        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.UserName == userName || u.Email == email);

        if (existingUser != null)
        {
            var existingMembership = await _context.OrganizationMembers
                .FirstOrDefaultAsync(m =>
                    m.OrganizationId == organizationId &&
                    m.UserId == existingUser.Id);

            if (existingMembership != null && existingMembership.Status == OrganizationMemberStatus.Active)
                throw new ConflictException("User already exists in organization.");

            if (existingMembership == null)
            {
                existingMembership = new OrganizationMember
                {
                    OrganizationId = organizationId,
                    UserId = existingUser.Id,
                    Role = dto.Role,
                    Status = OrganizationMemberStatus.Active,
                    JoinedAtUtc = DateTimeOffset.UtcNow
                };
                _context.OrganizationMembers.Add(existingMembership);
            }
            else
            {
                existingMembership.Role = dto.Role;
                existingMembership.Status = OrganizationMemberStatus.Active;
                existingMembership.JoinedAtUtc = DateTimeOffset.UtcNow;
            }

            await _context.SaveChangesAsync();
            return MapOrganizationUser(existingUser, existingMembership);
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
        var user = new User
        {
            UserName = userName,
            Email = email,
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
            throw new ConflictException("Username or email is already in use.");
        }

        var membership = new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId = user.Id,
            Role = dto.Role,
            Status = OrganizationMemberStatus.Active,
            JoinedAtUtc = DateTimeOffset.UtcNow
        };
        _context.OrganizationMembers.Add(membership);
        await _context.SaveChangesAsync();

        return MapOrganizationUser(user, membership);
    }

    public async Task<OrganizationUserResponseDto> UpdateOrganizationUserAsync(
        Guid organizationId,
        int userId,
        OrganizationUserUpdateDto dto)
    {
        var membership = await _context.OrganizationMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == userId);

        if (membership == null)
            throw new KeyNotFoundException("User not found.");

        var user = membership.User;
        var userName = dto.UserName?.Trim();
        var email = dto.Email?.Trim();

        if (!string.IsNullOrWhiteSpace(userName) || !string.IsNullOrWhiteSpace(email))
        {
            var hasConflict = await _context.Users.AnyAsync(u =>
                u.Id != userId &&
                ((userName != null && u.UserName == userName) ||
                 (email != null && u.Email == email)));

            if (hasConflict)
                throw new ConflictException("Username or email is already in use.");

            if (!string.IsNullOrWhiteSpace(userName))
                user.UserName = userName;

            if (!string.IsNullOrWhiteSpace(email))
                user.Email = email;
        }

        if (dto.Role.HasValue)
            membership.Role = dto.Role.Value;

        if (dto.Status.HasValue)
            membership.Status = dto.Status.Value;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new ConflictException("Username or email is already in use.");
        }

        if (dto.Status == OrganizationMemberStatus.Suspended)
            await RevokeRefreshTokensAsync(userId);

        return MapOrganizationUser(user, membership);
    }

    public async Task<bool> RemoveOrganizationUserAsync(Guid organizationId, int userId)
    {
        var membership = await _context.OrganizationMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == userId);

        if (membership == null)
            return false;

        membership.Status = OrganizationMemberStatus.Suspended;
        await _context.SaveChangesAsync();
        await RevokeRefreshTokensAsync(userId);

        return true;
    }

    public async Task<UserResponseDto> UpdateProfileAsync(int userId, UserProfileUpdateDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            throw new KeyNotFoundException("User not found.");

        var userName = dto.UserName.Trim();
        var email = dto.Email.Trim();

        var hasConflict = await _context.Users.AnyAsync(u =>
            u.Id != userId && (u.UserName == userName || u.Email == email));

        if (hasConflict)
            throw new ConflictException("Username or email is already in use.");

        user.UserName = userName;
        user.Email = email;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new ConflictException("Username or email is already in use.");
        }

        return MapUser(user);
    }

    public async Task ChangePasswordAsync(int userId, UserChangePasswordDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            throw new KeyNotFoundException("User not found.");

        var valid = BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash);
        if (!valid)
            throw new InvalidOperationException("Current password is incorrect.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        await _context.SaveChangesAsync();
    }

    public async Task<string> UpdateAvatarAsync(int userId, string avatarUrl)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            throw new KeyNotFoundException("User not found.");

        user.AvatarUrl = avatarUrl;
        await _context.SaveChangesAsync();

        return user.AvatarUrl ?? string.Empty;
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

    private async Task<AuthResponseDto> IssueTokensAsync(
        User user,
        Guid organizationId,
        OrganizationMemberRole orgRole,
        DateTimeOffset? nowOverride = null)
    {
        var accessToken = GenerateJwtToken(user, organizationId, orgRole, out var expiresAtUtc, out _);

        var now = nowOverride ?? DateTimeOffset.UtcNow;
        var refreshDays = _jwtOptions.RefreshTokenDays;

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
            User = MapUser(user, orgRole)
        };
    }

    private async Task<AuthResponseDto> IssuePlatformTokensAsync(
        User user,
        DateTimeOffset? nowOverride = null)
    {
        var accessToken = GeneratePlatformJwtToken(user, out var expiresAtUtc, out _);

        var now = nowOverride ?? DateTimeOffset.UtcNow;
        var refreshDays = _jwtOptions.RefreshTokenDays;

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
            User = MapUser(user)
        };
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

    private async Task<OrganizationMember?> GetActiveMembershipAsync(int userId, Guid organizationId)
    {
        return await _context.OrganizationMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == userId &&
                m.Status == OrganizationMemberStatus.Active);
    }

    private async Task<bool> IsOrganizationActiveAsync(Guid organizationId)
    {
        return await _context.Organizations
            .AsNoTracking()
            .AnyAsync(o => o.Id == organizationId && o.IsActive);
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

    private string GenerateJwtToken(
        User user,
        Guid organizationId,
        OrganizationMemberRole orgRole,
        out DateTimeOffset expiresAtUtc,
        out string jti)
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
            new(ClaimTypes.Role, user.Role.ToString()),
            new(TenantClaimTypes.OrganizationId, organizationId.ToString()),
            new(TenantClaimTypes.OrganizationRole, orgRole.ToString())
        };

        if (user.Role == UserRole.Admin)
            claims.Add(new Claim(TenantClaimTypes.IsPlatformAdmin, "true"));

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

    private string GeneratePlatformJwtToken(
        User user,
        out DateTimeOffset expiresAtUtc,
        out string jti)
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
            new(ClaimTypes.Role, user.Role.ToString()),
            new(TenantClaimTypes.IsPlatformAdmin, "true")
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

    private static OrganizationUserResponseDto MapOrganizationUser(User user, OrganizationMember membership)
    {
        return new OrganizationUserResponseDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            AvatarUrl = user.AvatarUrl,
            Role = membership.Role.ToString(),
            Status = membership.Status.ToString(),
            JoinedAtUtc = membership.JoinedAtUtc,
            IsPlatformAdmin = user.Role == UserRole.Admin
        };
    }

    private static UserResponseDto MapUser(User user, OrganizationMemberRole? orgRole = null)
    {
        return new UserResponseDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            AvatarUrl = user.AvatarUrl,
            Role = (orgRole?.ToString() ?? user.Role.ToString()),
            IsPlatformAdmin = user.Role == UserRole.Admin
        };
    }
}
