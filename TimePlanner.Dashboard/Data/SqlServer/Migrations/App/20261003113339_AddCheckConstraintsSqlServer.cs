using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimePlanner.Dashboard.Data.SqlServer.Migrations.App
{
    /// <inheritdoc />
    public partial class AddCheckConstraintsSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //repairs rows that would break the new constraints so the migration never fails
            migrationBuilder.Sql("DELETE FROM \"TimeEntries\" WHERE \"EndTime\" <= \"StartTime\";");
            migrationBuilder.Sql("UPDATE \"DaySessions\" SET \"EndedAt\" = \"StartedAt\" WHERE \"EndedAt\" < \"StartedAt\";");
            migrationBuilder.Sql("UPDATE \"SessionPauses\" SET \"EndedAt\" = \"StartedAt\" WHERE \"EndedAt\" < \"StartedAt\";");
            migrationBuilder.Sql("UPDATE \"UserSettings\" SET \"CheckInIntervalMinutes\" = 90 WHERE \"CheckInIntervalMinutes\" NOT BETWEEN 5 AND 480;");
            migrationBuilder.Sql("UPDATE \"UserSettings\" SET \"SnoozeMinutes\" = 10 WHERE \"SnoozeMinutes\" NOT BETWEEN 1 AND 60;");
            migrationBuilder.Sql("UPDATE \"UserSettings\" SET \"MaxSnoozes\" = 3 WHERE \"MaxSnoozes\" NOT BETWEEN 0 AND 10;");
            migrationBuilder.Sql("UPDATE \"UserSettings\" SET \"MaxSkipsPerDay\" = 3 WHERE \"MaxSkipsPerDay\" NOT BETWEEN 0 AND 10;");
            migrationBuilder.Sql("UPDATE \"UserSettings\" SET \"DailyGoalHours\" = 8 WHERE \"DailyGoalHours\" NOT BETWEEN 0.5 AND 24;");
            migrationBuilder.Sql("UPDATE \"UserSettings\" SET \"IgnoredCheckInMinutes\" = 5 WHERE \"IgnoredCheckInMinutes\" NOT BETWEEN 1 AND 60;");
            migrationBuilder.Sql("UPDATE \"UserSettings\" SET \"LunchStart\" = '12:00:00', \"LunchEnd\" = '13:00:00' WHERE \"LunchStart\" >= \"LunchEnd\";");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_UserId",
                table: "TimeEntries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserSettings_Goal",
                table: "UserSettings",
                sql: "\"DailyGoalHours\" BETWEEN 0.5 AND 24");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserSettings_Ignored",
                table: "UserSettings",
                sql: "\"IgnoredCheckInMinutes\" BETWEEN 1 AND 60");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserSettings_Interval",
                table: "UserSettings",
                sql: "\"CheckInIntervalMinutes\" BETWEEN 5 AND 480");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserSettings_Lunch",
                table: "UserSettings",
                sql: "\"LunchStart\" < \"LunchEnd\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserSettings_MaxSkips",
                table: "UserSettings",
                sql: "\"MaxSkipsPerDay\" BETWEEN 0 AND 10");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserSettings_MaxSnoozes",
                table: "UserSettings",
                sql: "\"MaxSnoozes\" BETWEEN 0 AND 10");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserSettings_Snooze",
                table: "UserSettings",
                sql: "\"SnoozeMinutes\" BETWEEN 1 AND 60");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_UserId_StartTime",
                table: "TimeEntries",
                columns: new[] { "UserId", "StartTime" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TimeEntries_EndAfterStart",
                table: "TimeEntries",
                sql: "\"EndTime\" > \"StartTime\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SessionPauses_EndAfterStart",
                table: "SessionPauses",
                sql: "\"EndedAt\" IS NULL OR \"EndedAt\" >= \"StartedAt\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DaySessions_EndAfterStart",
                table: "DaySessions",
                sql: "\"EndedAt\" IS NULL OR \"EndedAt\" >= \"StartedAt\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserSettings_Goal",
                table: "UserSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserSettings_Ignored",
                table: "UserSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserSettings_Interval",
                table: "UserSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserSettings_Lunch",
                table: "UserSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserSettings_MaxSkips",
                table: "UserSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserSettings_MaxSnoozes",
                table: "UserSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserSettings_Snooze",
                table: "UserSettings");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_UserId_StartTime",
                table: "TimeEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TimeEntries_EndAfterStart",
                table: "TimeEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SessionPauses_EndAfterStart",
                table: "SessionPauses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DaySessions_EndAfterStart",
                table: "DaySessions");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_UserId",
                table: "TimeEntries",
                column: "UserId");
        }
    }
}
