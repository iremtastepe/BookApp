using BookApp.Application.DTOs.External;
using BookApp.Application.Interfaces.ExternalServices;

namespace BookApp.Infrastructure.ExternalServices;

public class HybridBookLookupService : IBookLookupService
{
    private readonly GoogleBooksLookupService _googleService;
    private readonly OpenLibraryLookupService _openLibraryService;

    public HybridBookLookupService(GoogleBooksLookupService googleService, OpenLibraryLookupService openLibraryService)
    {
        _googleService = googleService;
        _openLibraryService = openLibraryService;
    }

    public async Task<ExternalBookDto?> SearchByIsbnAsync(string isbn)
    {
        // 1. Önce Google Books'a sor (Çünkü metinsel verisi daha zengin)
        var googleResult = await _googleService.SearchByIsbnAsync(isbn);

        // Eğer Google Books hiç bulamadıysa, tamamen Open Library'ye düş
        if (googleResult == null)
        {
            return await _openLibraryService.SearchByIsbnAsync(isbn);
        }

        // 2. Google Books buldu ama kapak fotoğrafı eksikse
        if (string.IsNullOrEmpty(googleResult.CoverImageUrl))
        {
            // Open Library'den sadece bu eksik kapağı tamamlamaya çalış
            var openLibraryResult = await _openLibraryService.SearchByIsbnAsync(isbn);
            if (openLibraryResult != null && !string.IsNullOrEmpty(openLibraryResult.CoverImageUrl))
            {
                googleResult.CoverImageUrl = openLibraryResult.CoverImageUrl;
            }
        }

        return googleResult;
    }
}
