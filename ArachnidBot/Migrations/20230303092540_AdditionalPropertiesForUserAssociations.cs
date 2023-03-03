using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArachnidBot.Migrations
{
    /// <inheritdoc />
    public partial class AdditionalPropertiesForUserAssociations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscordName",
                table: "UserAssociations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TelegramName",
                table: "UserAssociations",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscordName",
                table: "UserAssociations");

            migrationBuilder.DropColumn(
                name: "TelegramName",
                table: "UserAssociations");
        }
    }
}
