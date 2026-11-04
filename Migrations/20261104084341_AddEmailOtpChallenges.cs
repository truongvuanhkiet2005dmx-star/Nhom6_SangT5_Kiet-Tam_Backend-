using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_Nhom6_Kiet_Tam_App_QuanLyChiTieu.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailOtpChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailOtpChallenges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CodeSalt = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOtpChallenges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOtpChallenges_Email",
                table: "EmailOtpChallenges",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailOtpChallenges");
        }
    }
}
