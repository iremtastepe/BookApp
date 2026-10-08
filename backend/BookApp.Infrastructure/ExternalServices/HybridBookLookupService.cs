using BookApp.Application.DTOs.External;
using BookApp.Application.Exceptions;
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
        // 1. Önce Google Books'a sor. Hata verirse hemen çökmüyoruz, hatayı saklayıp Open Library'ye düşeceğiz.
        ExternalBookDto? googleResult = null;
        ExternalServiceUnavailableException? googleError = null;

        try
        {
            googleResult = await _googleService.SearchByIsbnAsync(isbn);
        }
        catch (ExternalServiceUnavailableException ex)
        {
            googleError = ex;
        }

        // 2. Google kitabı buldu
        if (googleResult is not null)
        {
            // Kapak eksikse Open Library'den sadece kapağı tamamlamaya çalış.
            // Bu sadece bir "tamamlama" olduğu için Open Library hata verirse umursamıyoruz,
            // elimizdeki Google verisiyle devam ediyoruz.
            if (string.IsNullOrEmpty(googleResult.CoverImageUrl))
            {
                try
                {
                    var openLibraryResult = await _openLibraryService.SearchByIsbnAsync(isbn);
                    if (openLibraryResult is not null && !string.IsNullOrEmpty(openLibraryResult.CoverImageUrl))
                    {
                        googleResult.CoverImageUrl = openLibraryResult.CoverImageUrl;
                    }
                }
                catch (ExternalServiceUnavailableException)
                {
                    // Bilinçli olarak yutuluyor: kapak olmadan da kitabı döndürebiliriz
                }
            }

            return googleResult;
        }

        // 3. Google bulamadı (null) ya da hata verdi: Open Library'ye düş.
        // Open Library hata verirse exception kendiliğinden yukarı çıkar -> 503.
        var fallbackResult = await _openLibraryService.SearchByIsbnAsync(isbn);

        if (fallbackResult is not null)
        {
            return fallbackResult; // 200
        }

        // 4. Open Library "bulamadım" dedi. Ama Google hata vermişse Google'ın cevabını hiç görmedik,
        // kitabın gerçekten olmadığından emin olamayız -> 404 değil 503.
        if (googleError is not null)
        {
            throw new ExternalServiceUnavailableException(
                googleError.ServiceName,
                googleError.Message,
                googleError);
        }

        // 5. İki servis de sağlıklı cevap verdi ve ikisi de bulamadı -> gerçekten yok (404)
        return null;
    }
}