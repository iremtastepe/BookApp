using BookApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;


namespace BookApp.Infrastructure.Data.Configurations;

public class UserBookConfiguration : IEntityTypeConfiguration<UserBook>

{
    public void Configure(EntityTypeBuilder<UserBook> builder)

    {
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
            .OnDelete(DeleteBehavior.Cascade);
            // Bir Book silinirse, ona bağlı tüm UserBook kayıtları da silinsin.
    }
}