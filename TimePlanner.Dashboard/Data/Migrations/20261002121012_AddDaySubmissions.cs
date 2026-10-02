using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimePlanner.Dashboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDaySubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DaySubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DaySubmissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DaySubmissions_AppUserId_Date",
                table: "DaySubmissions",
                columns: new[] { "AppUserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DaySubmissions_Date",
                table: "DaySubmissions",
                column: "Date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DaySubmissions");
        }
    }
}
