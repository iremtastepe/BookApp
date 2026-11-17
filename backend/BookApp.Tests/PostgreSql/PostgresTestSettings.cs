
namespace BookApp.Tests.PostgreSql;

public static class PostgresTestSettings
{
    public const string ConnectionEnvironmentVariable = "BOOKAPP_TEST_PG_CONNECTION";

    public const string RequiredDatabaseName = "BookAppDb_Test";

    public static string? RawConnectionString
        => Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);

    public static bool IsConfigured
        => !string.IsNullOrWhiteSpace(RawConnectionString);
}
