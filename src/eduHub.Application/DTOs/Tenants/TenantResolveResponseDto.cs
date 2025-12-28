using System.Collections.Generic;

namespace eduHub.Application.DTOs.Tenants;

public class TenantResolveResponseDto
{
    public List<TenantSummaryDto> Tenants { get; set; } = new();
}
