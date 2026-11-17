
using Npgsql;

namespace BookApp.Tests.PostgreSql;

// Test bağlantısının yanlışlıkla BookAppDb'ye
// veya uzak bir sunucuya yönelmesini engeller.
public static class PostgresTestConnectionGuard
{
    private static readonly string[] AllowedHosts = ["localhost", "127.0.0.1", "::1"];

    public static string Validate(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw Fail("Bağlantı dizesi boş.");

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception)
        {
            throw Fail("Bağlantı dizesi çözümlenemedi (içeriği güvenlik için gösterilmiyor).");
        }

        var database = builder.Database;
        if (!string.Equals(database, PostgresTestSettings.RequiredDatabaseName, StringComparison.Ordinal))
        {
            throw Fail($"Hedef veritabanı '{database ?? "(belirtilmemiş)"}'. " +
                       $"Yalnızca '{PostgresTestSettings.RequiredDatabaseName}' kabul edilir.");
        }

        var hosts = (builder.Host ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (hosts.Length == 0 || hosts.Any(h => !AllowedHosts.Contains(h, StringComparer.OrdinalIgnoreCase)))
            throw Fail("Sunucu yalnızca yerel makine olabilir (localhost, 127.0.0.1 veya ::1).");

        return connectionString;
    }

    private static InvalidOperationException Fail(string reason)
        => new("PostgreSQL test güvenlik koruması devreye girdi: hiçbir bağlantı açılmadı, " +
               $"hiçbir migration veya veri değişikliği yapılmadı. Neden: {reason} " +
               $"Ortam değişkenini ({PostgresTestSettings.ConnectionEnvironmentVariable}) kontrol edin.");
}
