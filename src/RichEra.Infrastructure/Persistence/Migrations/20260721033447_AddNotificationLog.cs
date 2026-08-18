using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RichEra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationLogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<byte>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    FireTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    PlatformNotificationId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationLogEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogEntries_DedupeKey",
                table: "NotificationLogEntries",
                column: "DedupeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogEntries_FireTimeUtc",
                table: "NotificationLogEntries",
                column: "FireTimeUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationLogEntries");
        }
    }
}
