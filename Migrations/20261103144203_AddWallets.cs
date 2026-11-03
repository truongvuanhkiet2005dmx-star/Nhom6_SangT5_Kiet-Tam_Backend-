using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_Nhom6_Kiet_Tam_App_QuanLyChiTieu.Migrations
{
    /// <inheritdoc />
    public partial class AddWallets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WalletId",
                table: "FamilyTransactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    InitialBalance = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyTransactions_WalletId",
                table: "FamilyTransactions",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_UserId",
                table: "Wallets",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_FamilyBudgets_FamilyGroups_FamilyGroupId",
                table: "FamilyBudgets",
                column: "FamilyGroupId",
                principalTable: "FamilyGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FamilyTransactions_Wallets_WalletId",
                table: "FamilyTransactions",
                column: "WalletId",
                principalTable: "Wallets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FamilyBudgets_FamilyGroups_FamilyGroupId",
                table: "FamilyBudgets");

            migrationBuilder.DropForeignKey(
                name: "FK_FamilyTransactions_Wallets_WalletId",
                table: "FamilyTransactions");

            migrationBuilder.DropTable(
                name: "Wallets");

            migrationBuilder.DropIndex(
                name: "IX_FamilyTransactions_WalletId",
                table: "FamilyTransactions");

            migrationBuilder.DropColumn(
                name: "WalletId",
                table: "FamilyTransactions");
        }
    }
}
