using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_Nhom6_Kiet_Tam_App_QuanLyChiTieu.Migrations
{
    /// <inheritdoc />
    public partial class AddAdjustmentDirection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdjustmentDirection",
                table: "FamilyTransactions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdjustmentDirection",
                table: "FamilyTransactions");
        }
    }
}
