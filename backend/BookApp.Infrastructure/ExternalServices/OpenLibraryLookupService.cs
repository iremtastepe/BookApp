using System.Text.Json;
using BookApp.Application.DTOs.External;
using BookApp.Application.Interfaces.ExternalServices;

namespace BookApp.Infrastructure.ExternalServices;

public class OpenLibraryLookupService : IBookLookupService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OpenLibraryLookupService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ExternalBookDto?> SearchByIsbnAsync(string isbn)
    {
        var requestUrl = $"https://openlibrary.org/api/books?bibkeys=ISBN:{isbn}&format=json&jscmd=data";
        var client = _httpClientFactory.CreateClient("OpenLibraryClient");
        
        var response = await client.GetAsync(requestUrl);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        var key = $"ISBN:{isbn}";
        if (!root.TryGetProperty(key, out var bookElement))
        {
            return null; // Kitap bulunamadı
        }

        var title = bookElement.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? "Bilinmeyen Kitap" : "Bilinmeyen Kitap";

        var author = "Bilinmeyen Yazar";
        if (bookElement.TryGetProperty("authors", out var authorsElement) && authorsElement.GetArrayLength() > 0)
        {
            var authorList = new List<string>();
            foreach (var authorNode in authorsElement.EnumerateArray())
            {
                if (authorNode.TryGetProperty("name", out var nameElement))
                {
                    var a = nameElement.GetString();
                    if (!string.IsNullOrEmpty(a)) authorList.Add(a);
                }
            }
            if (authorList.Any())
            {
                author = string.Join(", ", authorList);
            }
        }

        string? coverUrl = null;
        if (bookElement.TryGetProperty("cover", out var coverElement))
        {
            // Öncelik: large > medium > small
            if (coverElement.TryGetProperty("large", out var largeElement))
                coverUrl = largeElement.GetString();
            else if (coverElement.TryGetProperty("medium", out var mediumElement))
                coverUrl = mediumElement.GetString();
            else if (coverElement.TryGetProperty("small", out var smallElement))
                coverUrl = smallElement.GetString();
        }

        int? pageCount = null;
        if (bookElement.TryGetProperty("number_of_pages", out var pagesElement) && pagesElement.ValueKind == JsonValueKind.Number)
        {
            pageCount = pagesElement.GetInt32();
        }

        int? publishedYear = null;
        if (bookElement.TryGetProperty("publish_date", out var pubDateElement))
        {
            var pubDateStr = pubDateElement.GetString();
            if (!string.IsNullOrEmpty(pubDateStr))
            {
                // Open Library genellikle "1988" veya "Oct 1988" gibi değerler döner.
                // En son 4 haneli rakamı bulmak (yıl)
                var words = pubDateStr.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in words)
                {
                    if (word.Length == 4 && int.TryParse(word, out var year))
                    {
                        publishedYear = year;
                        break;
                    }
                }
            }
        }

        return new ExternalBookDto
        {
            Title = title,
            Author = author,
            Isbn = isbn,
            CoverImageUrl = coverUrl,
            Description = null, // Open Library jscmd=data genelde uzun açıklama dönmüyor
            PageCount = pageCount,
            PublishedYear = publishedYear,
            Genre = null // Kategoriler var ama formatlamak Google kadar standart değil, şimdilik null
        };
    }
}
