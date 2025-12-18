using System;
using System.Text;
using System.Text.Json;

namespace eduHub.Application.Common;

public static class CursorSerializer
{
    public static string Encode<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static bool TryDecode<T>(string? cursor, out T? payload)
    {
        payload = default;
        if (string.IsNullOrWhiteSpace(cursor))
            return false;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            payload = JsonSerializer.Deserialize<T>(json);
            return payload != null;
        }
        catch
        {
            payload = default;
            return false;
        }
    }
}
