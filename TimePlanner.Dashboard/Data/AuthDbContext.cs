using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TimePlanner.Dashboard.Data
{
    //-----------------------------
    //a login account, linked to the domain AppUser by AppUserId so AppDbContext stays untouched
    public class ApplicationUser : IdentityUser
    {
        public int? AppUserId { get; set; }
    }

    //-----------------------------
    //identity tables live in their own context and migration history, so they never collide with AppDbContext migrations
    public class AuthDbContext : IdentityDbContext<ApplicationUser>
    {
        public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }
    }
}