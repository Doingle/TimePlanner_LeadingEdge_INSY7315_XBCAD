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

        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<AuditEvent>(e =>
            {
                e.Property(x => x.Email).HasMaxLength(256);
                e.Property(x => x.Action).HasMaxLength(50).IsRequired();
                e.Property(x => x.Detail).HasMaxLength(500);
                e.Property(x => x.IpAddress).HasMaxLength(45);
                e.HasIndex(x => x.TimestampUtc);
                e.HasIndex(x => x.Action);
            });
        }
    }
}