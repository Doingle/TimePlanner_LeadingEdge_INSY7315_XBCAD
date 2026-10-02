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

        //a deactivated account keeps its history but can no longer sign in, on the website or the api
        public bool IsActive { get; set; } = true;

        //true while the password is a temporary one an administrator handed out, the person must choose their own before doing anything else
        public bool MustChangePassword { get; set; }
    }

    //-----------------------------
    //identity tables live in their own context and migration history, so they never collide with AppDbContext migrations
    public class AuthDbContext : IdentityDbContext<ApplicationUser>
    {
        public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

        //-----------------------------
        //lets provider specific subclasses pass their own options
        protected AuthDbContext(DbContextOptions options) : base(options) { }
        
        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        public DbSet<DaySubmission> DaySubmissions => Set<DaySubmission>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            //existing accounts stay active when the column is added
            builder.Entity<ApplicationUser>().Property(u => u.IsActive).HasDefaultValue(true);

            //a person submits a day once, sending it again only updates the time
            builder.Entity<DaySubmission>(e =>
            {
                e.HasIndex(x => new { x.AppUserId, x.Date }).IsUnique();
                e.HasIndex(x => x.Date);
            });

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