using Microsoft.AspNetCore.Identity;

namespace BookApp.Domain.Entities;

public class User : IdentityUser<int>
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<UserBook> UserBooks { get; set; } = new List<UserBook>();
}