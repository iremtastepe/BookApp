using BookApp.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BookApp.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<User, IdentityRole<int>, int>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Book> Books => Set<Book>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<UserBook> UserBooks => Set<UserBook>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity tablo isimleri
        builder.Entity<User>().ToTable("Users");
        builder.Entity<IdentityRole<int>>().ToTable("Roles");
        builder.Entity<IdentityUserRole<int>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<int>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<int>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<int>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<int>>().ToTable("UserTokens");

        // Bir kullanıcı aynı kitabı kitaplığına iki kez ekleyemesin
        builder.Entity<UserBook>()
            .HasIndex(ub => new { ub.UserId, ub.BookId })
            .IsUnique();

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}