using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RichEra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Transactions",
                type: "TEXT",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "Transactions");
        }
    }
}
