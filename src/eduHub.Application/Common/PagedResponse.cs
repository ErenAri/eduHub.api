using System.Collections.Generic;

namespace eduHub.Application.Common;

public class PagedResponse<T>
{
    public IEnumerable<T> Items { get; init; } = new List<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}
