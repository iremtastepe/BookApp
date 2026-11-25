using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookApp.Application.DTOs.Auth;
using BookApp.Application.DTOs.Books;
using BookApp.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BookApp.Tests;

public class BookRateLimitingEndpointTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public BookRateLimitingEndpointTests(
        CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        var username = $"ratelimit_{Guid.NewGuid():N}";
        var email = $"{username}@test.com";
        const string password = "Password123!";

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<User>>();

            var user = new User
            {
                UserName = username,
                Email = email
            };

            var result = await userManager.CreateAsync(
                user,
                password);

            Assert.True(
                result.Succeeded,
                string.Join(", ",
                    result.Errors.Select(e => e.Description)));
        }

        var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest
            {
                Email = email,
                Password = password
            });

        loginResponse.EnsureSuccessStatusCode();

        var auth = await loginResponse.Content
            .ReadFromJsonAsync<AuthResponse>();

        Assert.NotNull(auth);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                auth.Token);

        return client;
    }

    // Gecersiz istek kullanarak gercek kitap kaydi olusturmuyoruz.
    private static Task<HttpResponseMessage> SendBookCreateAsync(
        HttpClient client)
    {
        return client.PostAsJsonAsync(
            "/api/books",
            new CreateBookRequest
            {
                Title = "",
                Author = "Test Yazarı",
                PageCount = 250
            });
    }

    // Gecersiz ISBN kullanarak dis servislere gitmiyoruz.
    private static Task<HttpResponseMessage> SendIsbnSearchAsync(
        HttpClient client)
    {
        return client.GetAsync(
            "/api/books/search-by-isbn?isbn=invalid-isbn");
    }

    // 1. Kitap ekleme: ilk 10 istek gecer, 11. istek 429 olur.
    [Fact]
    public async Task BookCreate_EleventhRequest_Returns429()
    {
        using var client = await CreateAuthorizedClientAsync();

        for (var i = 0; i < 10; i++)
        {
            using var response = await SendBookCreateAsync(client);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);
        }

        using var limitedResponse = await SendBookCreateAsync(client);

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            limitedResponse.StatusCode);

        var body = await limitedResponse.Content
            .ReadAsStringAsync();

        Assert.Contains("Çok fazla istek", body);
    }

    // 2. ISBN arama: ilk 20 istek gecer, 21. istek 429 olur.
    [Fact]
    public async Task IsbnSearch_TwentyFirstRequest_Returns429()
    {
        using var client = await CreateAuthorizedClientAsync();

        for (var i = 0; i < 20; i++)
        {
            using var response = await SendIsbnSearchAsync(client);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);
        }

        using var limitedResponse = await SendIsbnSearchAsync(client);

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            limitedResponse.StatusCode);
    }

    // 3. Bir kullanicinin limiti digerini etkilememeli.
    [Fact]
    public async Task BookCreate_LimitsAreIndependentPerUser()
    {
        using var firstClient = await CreateAuthorizedClientAsync();
        using var secondClient = await CreateAuthorizedClientAsync();

        for (var i = 0; i < 10; i++)
        {
            using var response = await SendBookCreateAsync(firstClient);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);
        }

        using var blockedResponse = await SendBookCreateAsync(
            firstClient);

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            blockedResponse.StatusCode);

        using var otherUserResponse = await SendBookCreateAsync(
            secondClient);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            otherUserResponse.StatusCode);
    }

    // 4. Kitap ekleme limiti diger endpointleri etkilememeli.
    [Fact]
    public async Task BookCreate_LimitDoesNotBlockOtherEndpoints()
    {
        using var client = await CreateAuthorizedClientAsync();

        for (var i = 0; i < 10; i++)
        {
            using var response = await SendBookCreateAsync(client);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);
        }

        using var blockedResponse = await SendBookCreateAsync(client);

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            blockedResponse.StatusCode);

        // Kitap listeleme sinirlandirilmamis.
        using var listResponse = await client.GetAsync("/api/books");

        Assert.Equal(
            HttpStatusCode.OK,
            listResponse.StatusCode);

        // ISBN arama ayri bir politika kullaniyor.
        using var isbnResponse = await SendIsbnSearchAsync(client);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            isbnResponse.StatusCode);
    }
}
