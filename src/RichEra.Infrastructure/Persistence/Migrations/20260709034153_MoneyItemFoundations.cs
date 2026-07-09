using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RichEra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Money Log foundations. HAND-EDITED from the scaffold: EF diffs by name, so it proposed
    /// DropTable(IncomeSources) + CreateTable(Items) — which would erase every recurring rule.
    /// Rewritten as in-place renames so existing data survives:
    ///   IncomeSources                -> Items       (+ optional schedule, unit, bucket, micros)
    ///   Transactions.IncomeSourceId  -> ItemId      (+ Quantity, defaulting existing rows to 1)
    ///   Categories                   += Bucket      (macro roll-up)
    ///   Settings                     new            (key-value knobs, e.g. budget overrides)
    /// </summary>
    public partial class MoneyItemFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 1) IncomeSources becomes Items: a rename, so every recurring rule survives ----
            migrationBuilder.RenameTable(
                name: "IncomeSources",
                newName: "Items");

            migrationBuilder.RenameIndex(
                name: "IX_IncomeSources_AccountId",
                table: "Items",
                newName: "IX_Items_AccountId");

            migrationBuilder.RenameIndex(
                name: "IX_IncomeSources_CategoryId",
                table: "Items",
                newName: "IX_Items_CategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_IncomeSources_Direction_IsArchived",
                table: "Items",
                newName: "IX_Items_Direction_IsArchived");

            // ---- 2) New item fields (all nullable or defaulted — pure additive) ----
            migrationBuilder.AddColumn<string>(
                name: "Counterparty",
                table: "Items",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitName",
                table: "Items",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Bucket",
                table: "Items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AprPct",
                table: "Items",
                type: "TEXT",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Interest",
                table: "Items",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0); // InterestModel.None

            migrationBuilder.AddColumn<decimal>(
                name: "PrincipalBalance",
                table: "Items",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanPaymentsTotal",
                table: "Items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanPaymentsMade",
                table: "Items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedReturnPct",
                table: "Items",
                type: "TEXT",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FeesPerUnit",
                table: "Items",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTaxDeductible",
                table: "Items",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // ---- 3) The schedule becomes optional (SQLite: EF rebuilds the table in place) ----
            migrationBuilder.AlterColumn<int>(
                name: "Frequency",
                table: "Items",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "StartDate",
                table: "Items",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "TEXT");

            // ---- 4) Transactions: the rule link becomes the item link, plus serving count ----
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_IncomeSources_IncomeSourceId",
                table: "Transactions");

            migrationBuilder.RenameColumn(
                name: "IncomeSourceId",
                table: "Transactions",
                newName: "ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_Transactions_IncomeSourceId",
                table: "Transactions",
                newName: "IX_Transactions_ItemId");

            // Every pre-existing row is one serving of whatever it logged.
            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "Transactions",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Items_ItemId",
                table: "Transactions",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ---- 5) Category macro bucket + settings store ----
            migrationBuilder.AddColumn<int>(
                name: "Bucket",
                table: "Categories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort inverse. Note: items created WITHOUT a schedule (null Frequency)
            // cannot survive the return to NOT NULL columns — remove them first.
            migrationBuilder.Sql("DELETE FROM \"Items\" WHERE \"Frequency\" IS NULL OR \"StartDate\" IS NULL;");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropColumn(
                name: "Bucket",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Items_ItemId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Transactions");

            migrationBuilder.RenameColumn(
                name: "ItemId",
                table: "Transactions",
                newName: "IncomeSourceId");

            migrationBuilder.RenameIndex(
                name: "IX_Transactions_ItemId",
                table: "Transactions",
                newName: "IX_Transactions_IncomeSourceId");

            migrationBuilder.DropColumn(name: "Counterparty", table: "Items");
            migrationBuilder.DropColumn(name: "UnitName", table: "Items");
            migrationBuilder.DropColumn(name: "Bucket", table: "Items");
            migrationBuilder.DropColumn(name: "AprPct", table: "Items");
            migrationBuilder.DropColumn(name: "Interest", table: "Items");
            migrationBuilder.DropColumn(name: "PrincipalBalance", table: "Items");
            migrationBuilder.DropColumn(name: "PlanPaymentsTotal", table: "Items");
            migrationBuilder.DropColumn(name: "PlanPaymentsMade", table: "Items");
            migrationBuilder.DropColumn(name: "ExpectedReturnPct", table: "Items");
            migrationBuilder.DropColumn(name: "FeesPerUnit", table: "Items");
            migrationBuilder.DropColumn(name: "IsTaxDeductible", table: "Items");

            migrationBuilder.AlterColumn<int>(
                name: "Frequency",
                table: "Items",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "StartDate",
                table: "Items",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.RenameTable(
                name: "Items",
                newName: "IncomeSources");

            migrationBuilder.RenameIndex(
                name: "IX_Items_AccountId",
                table: "IncomeSources",
                newName: "IX_IncomeSources_AccountId");

            migrationBuilder.RenameIndex(
                name: "IX_Items_CategoryId",
                table: "IncomeSources",
                newName: "IX_IncomeSources_CategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_Items_Direction_IsArchived",
                table: "IncomeSources",
                newName: "IX_IncomeSources_Direction_IsArchived");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_IncomeSources_IncomeSourceId",
                table: "Transactions",
                column: "IncomeSourceId",
                principalTable: "IncomeSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
