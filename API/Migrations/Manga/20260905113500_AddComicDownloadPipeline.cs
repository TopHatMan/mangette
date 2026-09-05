using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <inheritdoc />
    public partial class AddComicDownloadPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ComicIssueEnd",
                table: "Mangas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ComicIssueStart",
                table: "Mangas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ComicDownloadJobs",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ChapterId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IndexerName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ReleaseTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    DownloadUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Protocol = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientName = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComicDownloadJobs", x => x.Key);
                    table.ForeignKey(
                        name: "FK_ComicDownloadJobs_Chapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "Chapters",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComicDownloadJobs_ChapterId",
                table: "ComicDownloadJobs",
                column: "ChapterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComicDownloadJobs");

            migrationBuilder.DropColumn(
                name: "ComicIssueEnd",
                table: "Mangas");

            migrationBuilder.DropColumn(
                name: "ComicIssueStart",
                table: "Mangas");
        }
    }
}
