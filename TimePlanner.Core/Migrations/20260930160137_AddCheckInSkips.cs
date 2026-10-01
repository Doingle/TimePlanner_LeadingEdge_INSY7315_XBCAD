using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimePlanner.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckInSkips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckInSkips",
                columns: table => new
                {
                    CheckInSkipId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DaySessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    SkippedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckInSkips", x => x.CheckInSkipId);
                    table.ForeignKey(
                        name: "FK_CheckInSkips_DaySessions_DaySessionId",
                        column: x => x.DaySessionId,
                        principalTable: "DaySessions",
                        principalColumn: "DaySessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckInSkips_DaySessionId",
                table: "CheckInSkips",
                column: "DaySessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckInSkips");
        }
    }
}
