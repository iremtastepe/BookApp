using BookApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookApp.Infrastructure.Data.Configurations;

public class ReadingGoalConfiguration : IEntityTypeConfiguration<ReadingGoal>
{
    public void Configure(EntityTypeBuilder<ReadingGoal> builder)
    {
        // Tablo adı ve CHECK constraintler:
        // - Hedef kitap sayısı daima pozitif tam sayı olmalıdır (> 0)
        // - Yıl geçerli bir takvim yılı olmalıdır (1 - 9999)
        builder.ToTable("ReadingGoals", t =>
        {
            t.HasCheckConstraint("CK_ReadingGoals_TargetBooks_Positive", "\"TargetBooks\" > 0");
            t.HasCheckConstraint("CK_ReadingGoals_Year_Range", "\"Year\" >= 1 AND \"Year\" <= 9999");
        });

        // Bir kullanıcının aynı takvim yılında en fazla bir hedef kaydı bulunabilir (UNIQUE index)
        builder.HasIndex(rg => new { rg.UserId, rg.Year })
            .IsUnique();

        // One-to-Many ilişki: Bir User silindiğinde ona ait hedefler de silinir
        builder.HasOne(rg => rg.User)
            .WithMany(u => u.ReadingGoals)
            .HasForeignKey(rg => rg.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
