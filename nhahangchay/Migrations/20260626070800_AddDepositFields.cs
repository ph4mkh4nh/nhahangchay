using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace nhahangchay.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DepositAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "OmakaseTier",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentStatus",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepositAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OmakaseTier",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Bookings");
        }
    }
}
