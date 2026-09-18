using BookApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookApp.Infrastructure.Data.Configurations;

public class NoteConfiguration : IEntityTypeConfiguration<Note>

{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.HasOne(n => n.UserBook)
        // "Her Note'un bir tane UserBook'u var" (bir not, bir kullanıcının bir kitapla ilişkisine bağlı).
            .WithMany(ub => ub.Notes)
            
            .HasForeignKey(n => n.UserBookId)
            .OnDelete(DeleteBehavior.Cascade);
            // Bir UserBook silinirse (kullanıcı kitabı kitaplığından kaldırırsa), ona bağlı notlar da silinsin.
    }
}