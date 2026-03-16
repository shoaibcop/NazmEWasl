using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NazmEWasl.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiLanguageSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnglishText",
                table: "Verses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HindiText",
                table: "Verses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectedLanguages",
                table: "Songs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnglishText",
                table: "Verses");

            migrationBuilder.DropColumn(
                name: "HindiText",
                table: "Verses");

            migrationBuilder.DropColumn(
                name: "SelectedLanguages",
                table: "Songs");
        }
    }
}
