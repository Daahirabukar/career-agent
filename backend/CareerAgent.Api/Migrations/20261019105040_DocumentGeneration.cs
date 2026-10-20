using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CareerAgent.Api.Migrations
{
    /// <inheritdoc />
    public partial class DocumentGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeneratedDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneratedDocuments_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReferenceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TextFileName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneratedDocumentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentFileName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ProfileVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReferenceIdsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Engine = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedDocumentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneratedDocumentVersions_GeneratedDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "GeneratedDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedDocuments_JobId_Kind",
                table: "GeneratedDocuments",
                columns: new[] { "JobId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedDocumentVersions_DocumentId_Number",
                table: "GeneratedDocumentVersions",
                columns: new[] { "DocumentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceDocuments_CreatedAt",
                table: "ReferenceDocuments",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeneratedDocumentVersions");

            migrationBuilder.DropTable(
                name: "ReferenceDocuments");

            migrationBuilder.DropTable(
                name: "GeneratedDocuments");
        }
    }
}
