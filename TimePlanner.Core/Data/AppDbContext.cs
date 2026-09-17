using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {

        }

        public DbSet<Company> Companies => Set<Company>();
        public DbSet<Project> Projects => Set<Project>();
        public DbSet<WorkTask> Tasks => Set<WorkTask>();
        public DbSet<AppUser> Users => Set<AppUser>();
        public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
        public DbSet<TimeSheet> TimeSheets => Set<TimeSheet>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // due to EF's conventions, WorkTask and AppUser won't get a key, so declaring every key explicitly is necessary
            modelBuilder.Entity<Company>().HasKey(c => c.CompanyId);
            modelBuilder.Entity<Project>().HasKey(p => p.ProjectID);
            modelBuilder.Entity<WorkTask>().HasKey(t => t.TaskID);
            modelBuilder.Entity<AppUser>().HasKey(u => u.UserId);
            modelBuilder.Entity<TimeEntry>().HasKey(e => e.TimeEntryId);
            modelBuilder.Entity<TimeSheet>().HasKey(s => s.TimeSheetId);

            modelBuilder.Entity<Project>()
                .HasOne(p => p.Company).WithMany(c => c.Projects)
                .HasForeignKey(p => p.CompanyId);

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
        }
    }
}
