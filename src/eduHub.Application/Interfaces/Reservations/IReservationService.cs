using eduHub.Application.Common;
using eduHub.Application.DTOs.Reservations;

namespace eduHub.Application.Interfaces.Reservations
{
    public interface IReservationService
    {
        // ------- READ --------

        Task<ReservationResponseDto?> GetByIdAsync(int id);

        Task<PagedResult<ReservationResponseDto>> SearchAsync(
            ReservationQueryParameters query,
            int? currentUserId = null);


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

        Task<ReservationResponseDto> ApproveAsync(int id);

        Task<ReservationResponseDto> RejectAsync(int id);
    }
}
