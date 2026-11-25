using BookApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookApp.Infrastructure.Data.Configurations;

public class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        // 7.4-C.5: CreatedByUserId iliskisi - User silinirse Book silinmesin, CreatedByUserId null olsun
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(b => b.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
