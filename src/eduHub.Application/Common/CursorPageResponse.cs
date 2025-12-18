using System.Collections.Generic;

namespace eduHub.Application.Common;

public class CursorPageResponse<T>
{
    public IEnumerable<T> Items { get; init; } = new List<T>();
    public int PageSize { get; init; }
    public string? NextCursor { get; init; }
    public bool HasMore { get; init; }
}
