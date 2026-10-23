using BookApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;


namespace BookApp.Infrastructure.Data.Configurations;

public class UserBookConfiguration : IEntityTypeConfiguration<UserBook>

{
    public void Configure(EntityTypeBuilder<UserBook> builder)

    {
        builder.ToTable("UserBooks", t =>
        {
            // 1. Tarih Hiyerarşisi: Yıl yoksa ay veya gün olamaz; ay yoksa gün olamaz
            t.HasCheckConstraint("CK_UserBooks_FinishedDate_Hierarchy",
                "(\"FinishedYear\" IS NULL AND \"FinishedMonth\" IS NULL AND \"FinishedDay\" IS NULL) OR " +
                "(\"FinishedYear\" IS NOT NULL AND \"FinishedMonth\" IS NULL AND \"FinishedDay\" IS NULL) OR " +
                "(\"FinishedYear\" IS NOT NULL AND \"FinishedMonth\" IS NOT NULL)");

            // 2. Yıl Aralığı: 1 ile 9999 arasında olmalı
            t.HasCheckConstraint("CK_UserBooks_FinishedYear_Range",
                "\"FinishedYear\" IS NULL OR (\"FinishedYear\" >= 1 AND \"FinishedYear\" <= 9999)");

            // 3. Ay Aralığı: 1 ile 12 arasında olmalı
            t.HasCheckConstraint("CK_UserBooks_FinishedMonth_Range",
                "\"FinishedMonth\" IS NULL OR (\"FinishedMonth\" >= 1 AND \"FinishedMonth\" <= 12)");

            // 4. Takvim Günü ve Artık Yıl Doğruluğu: 30 çeken aylar, 31 çeken aylar, Şubat (28/29 artık yıl)
            t.HasCheckConstraint("CK_UserBooks_FinishedDay_CalendarValid",
                "\"FinishedDay\" IS NULL OR (" +
                "\"FinishedDay\" >= 1 AND (" +
                "(\"FinishedMonth\" IN (1, 3, 5, 7, 8, 10, 12) AND \"FinishedDay\" <= 31) OR " +
                "(\"FinishedMonth\" IN (4, 6, 9, 11) AND \"FinishedDay\" <= 30) OR " +
                "(\"FinishedMonth\" = 2 AND (" +
                "(((\"FinishedYear\" % 4 = 0 AND \"FinishedYear\" % 100 <> 0) OR (\"FinishedYear\" % 400 = 0)) AND \"FinishedDay\" <= 29) OR " +
                "((NOT ((\"FinishedYear\" % 4 = 0 AND \"FinishedYear\" % 100 <> 0) OR (\"FinishedYear\" % 400 = 0))) AND \"FinishedDay\" <= 28)" +
                "))" +
                "))");
        });

        // Yıllık hedef ve aylık istatistik sorguları için birleşik (composite) indeksler
        builder.HasIndex(ub => new { ub.UserId, ub.Status, ub.FinishedYear });
        builder.HasIndex(ub => new { ub.UserId, ub.Status, ub.FinishedYear, ub.FinishedMonth });

        builder.HasIndex(ub => new { ub.UserId, ub.BookId })
            .IsUnique();
        // UserId ve BookId ikilisi birlikte benzersiz olsun
        // Yani UserId=1, BookId=5 sadece bir kere olabilir.

        builder.HasOne(ub => ub.User)
        // "Her UserBook'un bir tane User'ı var" (One-to-Many ilişkinin "One" tarafı).
            .WithMany(u => u.UserBooks)
            .HasForeignKey(ub => ub.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            // Bir User silinirse, ona ait tüm UserBook kayıtları da otomatik silinir

        builder.HasOne(ub => ub.Book)
        // Aynı mantık, bu sefer Book tarafı için.
            .WithMany(b => b.UserBooks)
            .HasForeignKey(ub => ub.BookId)
            .OnDelete(DeleteBehavior.Restrict);
            // Ortak katalogdaki Book, ilişkili UserBook varken silinemez (veri bütünlüğü koruması).
    }
}
