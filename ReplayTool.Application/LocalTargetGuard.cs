namespace ReplayTool.Application;

// Shared safety check: the replay tool writes directly to a database and publishes to a
// broker, so both targets must default to LOCAL and refuse a non-local host unless an
// explicit override is set — a captured prod scenario must never be replayed into a real
// environment.
public static class LocalTargetGuard
{
    public static bool IsLocalHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        return host.Trim().ToLowerInvariant() is "localhost" or "127.0.0.1" or "::1";
    }

    // Parses the Host= segment from a Npgsql-style connection string without taking
    // a hard dependency on NpgsqlConnectionStringBuilder in Application.
    public static bool IsLocalConnectionString(string connectionString)
    {
        foreach (var segment in connectionString.Split(';'))
        {
            var kv = segment.Trim().Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals("Host", StringComparison.OrdinalIgnoreCase))
                return IsLocalHost(kv[1]);
        }
        return true; // no explicit host → default local
    }
}
