using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <inheritdoc />
    public partial class AddMangaMonitorAndChapterCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastNewChapterCheck",
                table: "Mangas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Monitored",
                table: "Mangas",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NewChapterCheck",
                table: "Mangas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE Mangas
                SET Monitored = 1
                WHERE Key IN (
                    SELECT DISTINCT ObjId FROM MangaConnectorToManga WHERE UseForDownload = 1
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastNewChapterCheck",
                table: "Mangas");

            migrationBuilder.DropColumn(
                name: "Monitored",
                table: "Mangas");

            migrationBuilder.DropColumn(
                name: "NewChapterCheck",
                table: "Mangas");
        }
    }
}
