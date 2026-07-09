using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RichEra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavingsContributionLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SavingsSourceTransactionId",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SavingsSourceTransactionId",
                table: "Transactions",
                column: "SavingsSourceTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_SavingsSourceTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SavingsSourceTransactionId",
                table: "Transactions");
        }
    }
}
