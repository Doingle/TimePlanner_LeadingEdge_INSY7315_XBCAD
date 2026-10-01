using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {

        }

        //-----------------------------
        //lets provider specific subclasses pass their own options
        protected AppDbContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<Company> Companies => Set<Company>();
        public DbSet<Project> Projects => Set<Project>();
        public DbSet<WorkTask> Tasks => Set<WorkTask>();
        public DbSet<AppUser> Users => Set<AppUser>();
        public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
        public DbSet<TimeSheet> TimeSheets => Set<TimeSheet>();
        public DbSet<UserSettings> UserSettings => Set<UserSettings>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<DaySession> DaySessions => Set<DaySession>();
        public DbSet<SessionPause> SessionPauses => Set<SessionPause>();
        public DbSet<CheckInSkip> CheckInSkips => Set<CheckInSkip>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // due to EF's conventions, WorkTask and AppUser won't get a key, so declaring every key explicitly is necessary
            modelBuilder.Entity<Company>().HasKey(c => c.CompanyId);
            modelBuilder.Entity<Project>().HasKey(p => p.ProjectID);
            modelBuilder.Entity<WorkTask>().HasKey(t => t.TaskID);
            modelBuilder.Entity<AppUser>().HasKey(u => u.UserId);
            modelBuilder.Entity<TimeEntry>().HasKey(e => e.TimeEntryId);
            modelBuilder.Entity<TimeSheet>().HasKey(s => s.TimeSheetId);

            //company is required on every project, and a company that still has projects cannot be deleted
            modelBuilder.Entity<Project>()
                .HasOne(p => p.Company).WithMany(c => c.Projects)
                .HasForeignKey(p => p.CompanyId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<WorkTask>()
                .HasOne(t => t.Project).WithMany(p => p.Tasks)
                .HasForeignKey(t => t.ProjectID);

            modelBuilder.Entity<WorkTask>()
                .HasOne(t => t.AssignedUser).WithMany(u => u.AssignedTasks)
                .HasForeignKey(t => t.AssignedUserID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TimeEntry>()
                .HasOne(e => e.Task).WithMany(t => t.TimeEntries)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TimeEntry>()
                .HasOne(e => e.User).WithMany(u => u.TimeEntries)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TimeSheet>()
                .HasOne(s => s.User).WithMany(u => u.TimeSheets)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TimeSheet>()
                .HasOne(s => s.Project).WithMany()
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserSettings>().HasKey(s => s.UserSettingsId);

            modelBuilder.Entity<UserSettings>()
                .HasOne(s => s.User).WithOne(u => u.Settings)
                .HasForeignKey<UserSettings>(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AppUser>()
                .HasIndex(u => u.LocalAccountName)
                .IsUnique();

            modelBuilder.Entity<WorkTask>().Property(t => t.Status).HasConversion<string>();
            modelBuilder.Entity<Project>().Property(p => p.Status).HasConversion<string>();

            //categories form the activity tree, names are unique among siblings
            modelBuilder.Entity<Category>().HasKey(c => c.CategoryId);
            modelBuilder.Entity<Category>().HasIndex(c => new { c.ParentCategoryId, c.Name }).IsUnique();

            modelBuilder.Entity<Category>().HasIndex(c => c.Name).IsUnique().HasFilter("\"ParentCategoryId\" IS NULL");

            //a category that still has sub activities cannot be deleted
            modelBuilder.Entity<Category>()
                .HasOne(c => c.Parent).WithMany()
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            //every task needs a category, and a category still used by a task cannot be deleted
            modelBuilder.Entity<WorkTask>()
                .HasOne(t => t.Category).WithMany()
                .HasForeignKey(t => t.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            //the default categories ship inside the migration so every database has them without a separate seeding step
            modelBuilder.Entity<Category>().HasData(
                new Category { CategoryId = 1, Name = "Meeting", Colour = "#6366F1", SortOrder = 1, IsBillable = true },
                new Category { CategoryId = 2, Name = "Coding", Colour = "#7C3AED", SortOrder = 2, IsBillable = true },
                new Category { CategoryId = 4, Name = "Email", Colour = "#EA580C", SortOrder = 4, IsBillable = true },
                new Category { CategoryId = 5, Name = "Admin", Colour = "#71717A", SortOrder = 5, IsBillable = true },
                new Category { CategoryId = 6, Name = "Design", Colour = "#DB2777", SortOrder = 3, IsBillable = true },
                new Category { CategoryId = 7, Name = "Learning", Colour = "#0891B2", SortOrder = 6, IsBillable = false });

            //the ignored check in action is stored as its name, the same as statuses
            modelBuilder.Entity<UserSettings>().Property(s => s.IgnoredCheckInAction).HasConversion<string>();

            modelBuilder.Entity<DaySession>().HasKey(s => s.DaySessionId);
            modelBuilder.Entity<SessionPause>().HasKey(p => p.SessionPauseId);

            //day sessions belong to a user, and a user with tracked days cannot be deleted
            modelBuilder.Entity<DaySession>()
                .HasOne(s => s.User).WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            //a user can only have one open day session at a time
            modelBuilder.Entity<DaySession>()
                .HasIndex(s => s.UserId)
                .IsUnique()
                .HasFilter("\"EndedAt\" IS NULL");

            //pauses belong to their session and are deleted with it
            modelBuilder.Entity<SessionPause>()
                .HasOne(p => p.DaySession).WithMany(s => s.Pauses)
                .HasForeignKey(p => p.DaySessionId)
                .OnDelete(DeleteBehavior.Cascade);

            //a session can only have one pause in progress at a time
            modelBuilder.Entity<SessionPause>()
                .HasIndex(p => p.DaySessionId)
                .IsUnique()
                .HasFilter("\"EndedAt\" IS NULL");

            modelBuilder.Entity<CheckInSkip>().HasKey(s => s.CheckInSkipId);

            //skips belong to their day and go with it
            modelBuilder.Entity<CheckInSkip>()
                .HasOne(s => s.DaySession).WithMany()
                .HasForeignKey(s => s.DaySessionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
