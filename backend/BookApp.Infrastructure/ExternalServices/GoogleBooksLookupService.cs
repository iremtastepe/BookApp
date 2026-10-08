using System.Text.Json;
using BookApp.Application.DTOs.External;
using BookApp.Application.Exceptions;
using BookApp.Application.Interfaces.ExternalServices;
using Microsoft.Extensions.Configuration;

namespace BookApp.Infrastructure.ExternalServices;

public class GoogleBooksLookupService : IBookLookupService
{
    private const string ServiceName = "Google Books";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public GoogleBooksLookupService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<ExternalBookDto?> SearchByIsbnAsync(string isbn)
    {
        var apiKey = _configuration["ExternalApis:GoogleBooks:ApiKey"];
        var requestUrl = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{isbn}";

        if (!string.IsNullOrEmpty(apiKey))
        {
            requestUrl += $"&key={apiKey}";
        }

        string content;
        try
        {
            var client = _httpClientFactory.CreateClient("GoogleBooksClient");
            var response = await client.GetAsync(requestUrl);

            if (!response.IsSuccessStatusCode)
            {
                throw new ExternalServiceUnavailableException(
                    ServiceName,
                    $"{ServiceName} {(int)response.StatusCode} koduyla yanıt verdi.");
            }

            content = await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalServiceUnavailableException(ServiceName, $"{ServiceName} servisine ulaşılamadı.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new ExternalServiceUnavailableException(ServiceName, $"{ServiceName} zamanında yanıt vermedi.", ex);
        }

        using var jsonDoc = ParseOrThrow(content);
        var root = jsonDoc.RootElement;

        // Eğer totalItems 0 ise kitap gerçekten bulunamamıştır
        if (!root.TryGetProperty("totalItems", out var totalItemsElement) || totalItemsElement.GetInt32() == 0)
        {
            return null;
        }

        if (!root.TryGetProperty("items", out var itemsElement) || itemsElement.GetArrayLength() == 0)
        {
            return null;
        }

        var firstItem = itemsElement[0];
        if (!firstItem.TryGetProperty("volumeInfo", out var volumeInfo))
        {
            return null;
        }

        // Title
        var title = volumeInfo.TryGetProperty("title", out var titleElement)
            ? titleElement.GetString() ?? "Bilinmeyen Kitap"
            : "Bilinmeyen Kitap";

        // Authors
        var author = "Bilinmeyen Yazar";
        if (volumeInfo.TryGetProperty("authors", out var authorsElement) && authorsElement.GetArrayLength() > 0)
        {
            var authorList = new List<string>();
            foreach (var authorNode in authorsElement.EnumerateArray())
            {
                var a = authorNode.GetString();
                if (!string.IsNullOrEmpty(a)) authorList.Add(a);
            }
            if (authorList.Any())
            {
                author = string.Join(", ", authorList);
            }
        }

        // CoverImageUrl
        string? coverUrl = null;
        if (volumeInfo.TryGetProperty("imageLinks", out var imageLinks))
        {
            if (imageLinks.TryGetProperty("thumbnail", out var thumbnailElement))
            {
                coverUrl = thumbnailElement.GetString();
                // Google Books bazen http döndürür, frontend'de mixed content hatası olmasın diye https yapıyoruz
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    coverUrl = coverUrl.Replace("http://", "https://");
                }
            }
        }

        // Description
        string? description = null;
        if (volumeInfo.TryGetProperty("description", out var descElement))
        {
            description = descElement.GetString();
        }

        // PageCount
        int? pageCount = null;
        if (volumeInfo.TryGetProperty("pageCount", out var pageCountElement) && pageCountElement.ValueKind == JsonValueKind.Number)
        {
            pageCount = pageCountElement.GetInt32();
        }

        // PublishedYear
        int? publishedYear = null;
        if (volumeInfo.TryGetProperty("publishedDate", out var pubDateElement))
        {
            var pubDateStr = pubDateElement.GetString();
            if (!string.IsNullOrEmpty(pubDateStr) && pubDateStr.Length >= 4)
            {
                if (int.TryParse(pubDateStr.Substring(0, 4), out var year))
                {
                    publishedYear = year;
                }
            }
        }

        // Genre
        string? genre = null;
        if (volumeInfo.TryGetProperty("categories", out var categoriesElement) && categoriesElement.GetArrayLength() > 0)
        {
            genre = categoriesElement[0].GetString();
        }

        return new ExternalBookDto
        {
            Title = title,
            Author = author,
            Isbn = isbn,
            CoverImageUrl = coverUrl,
            Description = description,
            PageCount = pageCount,
            PublishedYear = publishedYear,
            Genre = genre
        };
    }

    // Bozuk/beklenmeyen JSON gelirse bunu "servis bozuk" hatasına çevirir
    private static JsonDocument ParseOrThrow(string content)
    {
        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new ExternalServiceUnavailableException(ServiceName, $"{ServiceName} geçersiz bir yanıt döndürdü.", ex);
        }
    }
}