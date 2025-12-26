using eduHub.Application.DTOs.Organizations;
using eduHub.Domain.Entities;

namespace eduHub.Application.Interfaces.Organizations;

public interface IOrganizationInviteService
{
    Task<(OrganizationInvite Invite, string Token)> CreateAsync(
        Guid organizationId,
        OrganizationInviteCreateDto dto,
        int createdByUserId);
    Task<OrganizationInvite?> GetByIdAsync(Guid inviteId);
    Task RevokeAsync(Guid inviteId, int revokedByUserId);
}
