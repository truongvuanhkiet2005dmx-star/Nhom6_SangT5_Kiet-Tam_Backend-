using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_Nhom6_Kiet_Tam_App_QuanLyChiTieu.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ToWalletId",
                table: "FamilyTransactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyTransactions_ToWalletId",
                table: "FamilyTransactions",
                column: "ToWalletId");

            migrationBuilder.AddForeignKey(
                name: "FK_FamilyTransactions_Wallets_ToWalletId",
                table: "FamilyTransactions",
                column: "ToWalletId",
                principalTable: "Wallets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FamilyTransactions_Wallets_ToWalletId",
                table: "FamilyTransactions");

            migrationBuilder.DropIndex(
                name: "IX_FamilyTransactions_ToWalletId",
                table: "FamilyTransactions");

            migrationBuilder.DropColumn(
                name: "ToWalletId",
                table: "FamilyTransactions");
        }
    }
}
