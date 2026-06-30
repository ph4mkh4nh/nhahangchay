using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace nhahangchay.Migrations
{
    /// <inheritdoc />
    public partial class AddIsReadField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRead",
                table: "ChatMessages",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRead",
                table: "ChatMessages");
        }
    }
}
