using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NazmEWasl.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddVerseKeywordsAndSongProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeywordsJson",
                table: "Verses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTranslationProvider",
                table: "Songs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeywordsJson",
                table: "Verses");

            migrationBuilder.DropColumn(
                name: "LastTranslationProvider",
                table: "Songs");
        }
    }
}
