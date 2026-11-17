
namespace BookApp.Tests.PostgreSql;

public class PostgresTestConnectionGuardTests
{
    private const string FakeSecret = "FAKE_TEST_PASSWORD";

    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=BookAppDb;Username=postgres;Password=FAKE_TEST_PASSWORD")]
    [InlineData("Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=FAKE_TEST_PASSWORD")]
    [InlineData("Host=localhost;Port=5432;Database=bookappdb_test;Username=postgres;Password=FAKE_TEST_PASSWORD")]
    [InlineData("Host=localhost;Port=5432;Database=BookAppDb_Test2;Username=postgres;Password=FAKE_TEST_PASSWORD")]
    [InlineData("Host=db.example.com;Port=5432;Database=BookAppDb_Test;Username=postgres;Password=FAKE_TEST_PASSWORD")]
    [InlineData("Host=localhost;Port=5432;Username=postgres;Password=FAKE_TEST_PASSWORD")]
    [InlineData("Database=BookAppDb_Test;Username=postgres;Password=FAKE_TEST_PASSWORD")]
    [InlineData("this is not a connection string;Password=FAKE_TEST_PASSWORD")]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsUnsafeConnectionStrings_WithoutLeakingSecrets(
        string connectionString)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => PostgresTestConnectionGuard.Validate(connectionString));

        Assert.DoesNotContain(FakeSecret, ex.Message);
    }

    [Fact]
    public void Validate_AcceptsLocalTestDatabase()
    {
        foreach (var host in new[] { "localhost", "127.0.0.1", "LOCALHOST" })
        {
            var cs = $"Host={host};Port=5432;Database=BookAppDb_Test;Username=bookapp_test;Password={FakeSecret}";

            Assert.Equal(cs, PostgresTestConnectionGuard.Validate(cs));
        }
    }
}
