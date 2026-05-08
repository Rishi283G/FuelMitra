using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDsmProfilesAndCreditorRepayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreditorRepayments",
                columns: table => new
                {
                    CreditorRepaymentId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepaymentDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreditorName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PaymentMode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ChequeNo = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Amount = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditorRepayments", x => x.CreditorRepaymentId);
                });

            migrationBuilder.CreateTable(
                name: "DsmProfiles",
                columns: table => new
                {
                    DsmProfileId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DsmName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DsmProfiles", x => x.DsmProfileId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DsmProfiles_DsmName",
                table: "DsmProfiles",
                column: "DsmName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditorRepayments");

            migrationBuilder.DropTable(
                name: "DsmProfiles");
        }
    }
}
