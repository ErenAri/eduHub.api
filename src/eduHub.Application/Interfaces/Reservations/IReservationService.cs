using eduHub.Application.Common;
using eduHub.Application.DTOs.Reservations;

namespace eduHub.Application.Interfaces.Reservations
{
    public interface IReservationService
    {
        // ------- READ --------

        Task<ReservationResponseDto?> GetByIdAsync(int id);

        Task<PagedResult<ReservationResponseDto>> GetByRoomIdPagedAsync(
            int roomId,
            int page,
            int pageSize);

        Task<PagedResult<ReservationResponseDto>> SearchAsync(
            int? buildingId,
            int? roomId,
            DateTime? startTimeUtc,
            DateTime? endTimeUtc,
            int page,
            int pageSize);

        Task<List<ReservationResponseDto>> GetByRoomAsync(int roomId);

        Task<PagedResult<ReservationResponseDto>> GetMyReservationsAsync(
            int currentUserId,
            int page,
            int pageSize);


        // ------- WRITE (Ownership-aware) --------

        Task<ReservationResponseDto> CreateAsync(
            ReservationCreateDto dto,
            int createdByUserId);

        Task<ReservationResponseDto> UpdateAsync(
            int id,
            ReservationUpdateDto dto,
            int currentUserId,
            bool isAdmin);

        Task<bool> DeleteAsync(
            int id,
            int currentUserId,
            bool isAdmin);
    }
}
