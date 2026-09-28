using BookApp.Application.Interfaces.ExternalServices;
using BookApp.Infrastructure.ExternalServices;
using Microsoft.Extensions.DependencyInjection;

namespace BookApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // İnternete çıkacak servisimiz (HttpClientFactory)
        services.AddHttpClient();

        // İşçilerimizi kendi hallerinde (sınıf olarak) kaydediyoruz
        services.AddScoped<GoogleBooksLookupService>();
        services.AddScoped<OpenLibraryLookupService>();
        
        // Şefimizi (Hybrid) asıl aranılan kitap servisi (IBookLookupService) olarak kaydediyoruz
        services.AddScoped<IBookLookupService, HybridBookLookupService>();

        return services;
    }
}
