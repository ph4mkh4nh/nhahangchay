using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace nhahangchay.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudinaryPublicIdToDish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloudinaryPublicId",
                table: "Dishes",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloudinaryPublicId",
                table: "Dishes");
        }
    }
}
