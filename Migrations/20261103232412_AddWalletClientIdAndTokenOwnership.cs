using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_Nhom6_Kiet_Tam_App_QuanLyChiTieu.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletClientIdAndTokenOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                table: "Wallets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_UserId_ClientId",
                table: "Wallets",
                columns: new[] { "UserId", "ClientId" },
                unique: true);

            migrationBuilder.Sql(
                "DELETE FROM \"DeviceTokens\" WHERE \"Id\" NOT IN (SELECT MAX(\"Id\") FROM \"DeviceTokens\" GROUP BY \"Token\");");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTokens_Token",
                table: "DeviceTokens",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Wallets_UserId_ClientId",
                table: "Wallets");

            migrationBuilder.DropIndex(
                name: "IX_DeviceTokens_Token",
                table: "DeviceTokens");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "Wallets");
        }
    }
}
