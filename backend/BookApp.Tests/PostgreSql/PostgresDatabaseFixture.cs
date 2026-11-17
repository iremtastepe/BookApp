
using BookApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookApp.Tests.PostgreSql;

// PostgreSQL test veritabanının güvenli hazırlanması.
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    public bool IsAvailable { get; private set; }

    public string ConnectionString { get; private set; } = string.Empty;

    public List<string> AppliedMigrations { get; private set; } = new();

    public async Task InitializeAsync()
    {
        // Bağlantı tanımlı değilse işlem yapma.
        if (!PostgresTestSettings.IsConfigured)
            return;

        // Bağlantı açılmadan önce güvenlik kontrolü.
        ConnectionString = PostgresTestConnectionGuard.Validate(
            PostgresTestSettings.RawConnectionString);

        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();

            // Doğru veritabanına ve doğru kullanıcıya
            // bağlandığımızı kontrol et.
            await using var command = new NpgsqlCommand(
                "SELECT current_database(), current_user",
                connection);

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync() ||
                reader.GetString(0) != PostgresTestSettings.RequiredDatabaseName ||
                reader.GetString(1) != "bookapp_test")
            {
                throw new InvalidOperationException(
                    "PostgreSQL güvenlik kontrolü başarısız. " +
                    "Veritabanı veya kullanıcı yanlış. " +
                    "Hiçbir migration çalıştırılmadı.");
            }
        }

        // Mevcut şemayı silmeden migration'ları uygula.
        await using var context = CreateContext();

        await context.Database.MigrateAsync();

        AppliedMigrations =
            (await context.Database.GetAppliedMigrationsAsync()).ToList();

        IsAvailable = true;
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new AppDbContext(options);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// PostgreSQL entegrasyon testleri aynı fixture'ı paylaşır.
[CollectionDefinition(Name)]
public sealed class PostgresCollection
    : ICollectionFixture<PostgresDatabaseFixture>
{
    public const string Name = "PostgreSQL integration";
}
