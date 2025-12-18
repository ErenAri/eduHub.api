using System;
using System.Collections.Generic;

namespace eduHub.Application.Common;

public class CursorPageResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public string? NextCursor { get; init; }
    public int PageSize { get; init; }
    public bool HasMore { get; init; }
}
