using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitPineLabCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CreditCardMorning",
                table: "PaymentCollections",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "CreditCardNight",
                table: "PaymentCollections",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            // Copy existing CreditCard data to CreditCardMorning
            migrationBuilder.Sql("UPDATE PaymentCollections SET CreditCardMorning = CreditCard");

            migrationBuilder.DropColumn(
                name: "CreditCard",
                table: "PaymentCollections");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CreditCard",
                table: "PaymentCollections",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.Sql("UPDATE PaymentCollections SET CreditCard = CreditCardMorning + CreditCardNight");

            migrationBuilder.DropColumn(
                name: "CreditCardMorning",
                table: "PaymentCollections");

            migrationBuilder.DropColumn(
                name: "CreditCardNight",
                table: "PaymentCollections");
        }
    }
}
