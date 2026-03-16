using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NazmEWasl.Web.Migrations
{
    /// <inheritdoc />
    public partial class DropEnglishGlossAndTranslationNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnglishGloss",
                table: "Verses");

            migrationBuilder.DropColumn(
                name: "TranslationNotes",
                table: "Verses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnglishGloss",
                table: "Verses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TranslationNotes",
                table: "Verses",
                type: "TEXT",
                nullable: true);
        }
    }
}
