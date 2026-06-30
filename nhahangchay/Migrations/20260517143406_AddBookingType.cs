using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace nhahangchay.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BookingType",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookingType",
                table: "Bookings");
        }
    }
}
