using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_Nhom6_Kiet_Tam_App_QuanLyChiTieu.Migrations
{
    /// <inheritdoc />
    public partial class EnableLockoutForExistingUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE AspNetUsers SET LockoutEnabled = 1 WHERE LockoutEnabled = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Preserve existing account data and lockout preferences; this data-only migration
            // intentionally has no destructive reverse operation.
        }
    }
}
