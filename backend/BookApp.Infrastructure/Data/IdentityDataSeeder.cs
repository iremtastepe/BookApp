using BookApp.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookApp.Infrastructure.Data;

public static class IdentityDataSeeder
{
    public const string AdminRoleName = "Admin";

    public static async Task SeedAdminAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("IdentityDataSeeder");

        // 1. Admin rolünün varlığını sağla (idempotent)
        if (!await roleManager.RoleExistsAsync(AdminRoleName))
        {
            await roleManager.CreateAsync(new IdentityRole<int>(AdminRoleName));
            logger?.LogInformation("Admin rolü oluşturuldu.");
        }

        // 2. Gizli yapılandırmadan (Environment Variables / User Secrets) ilk Admin bilgilerini oku
        var adminEmail = config["BOOKAPP_ADMIN_EMAIL"] ?? config["AdminUser:Email"];
        var adminPassword = config["BOOKAPP_ADMIN_PASSWORD"] ?? config["AdminUser:Password"];
        var adminUsername = config["BOOKAPP_ADMIN_USERNAME"] ?? config["AdminUser:Username"] ?? "admin";

        // Yapılandırma tanımlanmamışsa güvenli şekilde çıkış yap (kaynak kodda şifre yok)
        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            return;
        }

        // 3. Güvenlik Şartı: Mevcut kullanıcı kontrolü
        var existingUser = await userManager.FindByEmailAsync(adminEmail);
        if (existingUser != null)
        {
            // Kullanıcı zaten varsa:
            // Yetkisiz ayrıcalık yükseltmesini (privilege escalation) engellemek için:
            // Zaten normal hesap olarak açılmış veya daha önce Admin yetkisi alınmış bir hesaba
            // yeniden başlatmalarda otomatik yetki verilmez.
            return;
        }

        // 4. Kullanıcı hiç yoksa: İlk kurulumda güvenli bir Admin hesabı oluştur ve rolü ata
        var newAdmin = new User
        {
            UserName = adminUsername,
            Email = adminEmail
        };

        var createResult = await userManager.CreateAsync(newAdmin, adminPassword);
        if (createResult.Succeeded)
        {
            await userManager.AddToRoleAsync(newAdmin, AdminRoleName);
            logger?.LogInformation("İlk kurulum Admin kullanıcısı oluşturuldu ve yetkilendirildi.");
        }
        else
        {
            logger?.LogWarning("İlk Admin kullanıcısı oluşturulamadı: {Errors}",
                string.Join(", ", createResult.Errors.Select(e => e.Description)));
        }
    }
}
