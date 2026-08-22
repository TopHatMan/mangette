using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <inheritdoc />
    public partial class RemoveMangaworld : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM MangaConnectorToChapter WHERE MangaConnectorName = 'Mangaworld';");
            migrationBuilder.Sql("DELETE FROM MangaConnectorToManga WHERE MangaConnectorName = 'Mangaworld';");
            migrationBuilder.Sql("DELETE FROM MangaConnector WHERE Name = 'Mangaworld';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
