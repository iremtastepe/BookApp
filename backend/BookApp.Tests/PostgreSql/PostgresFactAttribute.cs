
namespace BookApp.Tests.PostgreSql;

// Ortam değişkeni yoksa PostgreSQL testlerini atlar.
// Bağlantı tanımlıysa testlerin çalışmasına izin verir.
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!PostgresTestSettings.IsConfigured)
        {
            Skip = $"{PostgresTestSettings.ConnectionEnvironmentVariable} tanımlı değil; " +
                   "PostgreSQL entegrasyon testi atlandı.";
        }
    }
}
