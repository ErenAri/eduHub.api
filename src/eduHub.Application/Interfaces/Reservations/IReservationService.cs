using eduHub.Application.Common;
using eduHub.Application.DTOs.Reservations;

namespace eduHub.Application.Interfaces.Reservations;

public interface IReservationService
{
    Task<ReservationResponseDto?> GetByIdAsync(int id, int currentUserId, bool canViewAll);
    Task<CursorPageResult<ReservationResponseDto>> SearchAsync(
        ReservationQueryParameters query,
        int? currentUserId,
        bool canViewAll);
    Task<ReservationResponseDto> CreateAsync(
        ReservationCreateDto dto,
        int createdByUserId);
    Task<ReservationResponseDto> UpdateAsync(
        int id,
        ReservationUpdateDto dto,
        int currentUserId,
        bool canManage);
    Task<bool> DeleteAsync(
        int id,
        int currentUserId,
        bool canManage);
    Task<ReservationResponseDto> ApproveAsync(int id, int approverUserId);
    Task<ReservationResponseDto> RejectAsync(int id, int approverUserId);
}
