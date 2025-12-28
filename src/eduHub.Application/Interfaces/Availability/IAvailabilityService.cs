using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using eduHub.Application.DTOs.Availability;

namespace eduHub.Application.Interfaces.Availability;

public interface IAvailabilityService
{
    Task<IReadOnlyList<AvailabilityWindowDto>> GetBuildingHoursAsync(int buildingId);
    Task<IReadOnlyList<AvailabilityWindowDto>> GetRoomHoursAsync(int roomId);
    Task<IReadOnlyList<AvailabilityWindowDto>> UpdateBuildingHoursAsync(
        int buildingId,
        IReadOnlyList<AvailabilityWindowDto> windows,
        int updatedByUserId);
    Task<IReadOnlyList<AvailabilityWindowDto>> UpdateRoomHoursAsync(
        int roomId,
        IReadOnlyList<AvailabilityWindowDto> windows,
        int updatedByUserId);
    Task<IReadOnlyList<AvailabilityBlackoutResponseDto>> GetBlackoutsAsync(
        int? buildingId,
        int? roomId,
        DateTimeOffset? startTimeUtc,
        DateTimeOffset? endTimeUtc);
    Task<AvailabilityBlackoutResponseDto> CreateBlackoutAsync(
        AvailabilityBlackoutCreateDto dto,
        int createdByUserId);
    Task<bool> DeleteBlackoutAsync(int id, int deletedByUserId);
    Task<IReadOnlyList<AvailabilitySlotDto>> GetRoomAvailabilityAsync(
        int roomId,
        DateTimeOffset startTimeUtc,
        DateTimeOffset endTimeUtc);
    Task ValidateReservationAsync(
        int roomId,
        DateTimeOffset startTimeUtc,
        DateTimeOffset endTimeUtc,
        int? excludeReservationId = null);
    Task<bool> IsRoomAvailableAsync(
        int roomId,
        DateTimeOffset startTimeUtc,
        DateTimeOffset endTimeUtc,
        int? excludeReservationId = null);
}
