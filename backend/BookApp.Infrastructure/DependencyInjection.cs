using BookApp.Application.Interfaces.ExternalServices;
using BookApp.Infrastructure.ExternalServices;
using Microsoft.Extensions.DependencyInjection;

namespace BookApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // Servislerin istediği isimli client'lar. Timeout, dış servis cevap vermezse
        // kullanıcının sonsuza kadar beklememesi için.
        services.AddHttpClient("GoogleBooksClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient("OpenLibraryClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // İşçilerimizi kendi hallerinde (sınıf olarak) kaydediyoruz
        services.AddScoped<GoogleBooksLookupService>();
        services.AddScoped<OpenLibraryLookupService>();

        // Şefimizi (Hybrid) asıl aranılan kitap servisi (IBookLookupService) olarak kaydediyoruz
        services.AddScoped<IBookLookupService, HybridBookLookupService>();

        return services;
    }
}