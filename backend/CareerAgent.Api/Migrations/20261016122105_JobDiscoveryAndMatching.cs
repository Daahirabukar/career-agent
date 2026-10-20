using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CareerAgent.Api.Migrations
{
    /// <inheritdoc />
    public partial class JobDiscoveryAndMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiAssessment",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiModel",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessmentKey",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CanonicalUrl",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "Deadline",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Eligibility",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmploymentType",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsImported",
                table: "Jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MatchDetails",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MatchReason",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PossibleDuplicateOf",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileVersion",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "Jobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScoredAt",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SeenAt",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkplaceType",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "DiscoveryRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Added = table.Column<int>(type: "INTEGER", nullable: false),
                    Updated = table.Column<int>(type: "INTEGER", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobOccurrences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobOccurrences_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_CanonicalUrl",
                table: "Jobs",
                column: "CanonicalUrl");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Score",
                table: "Jobs",
                column: "Score");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryRuns_CreatedAt",
                table: "DiscoveryRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_JobOccurrences_JobId",
                table: "JobOccurrences",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_JobOccurrences_SourceId_ExternalId",
                table: "JobOccurrences",
                columns: new[] { "SourceId", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscoveryRuns");

            migrationBuilder.DropTable(
                name: "JobOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_CanonicalUrl",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_Score",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AiAssessment",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AiModel",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AssessmentKey",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CanonicalUrl",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Deadline",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Eligibility",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "EmploymentType",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "IsImported",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "MatchDetails",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "MatchReason",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "PossibleDuplicateOf",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ProfileVersion",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ScoredAt",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SeenAt",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "WorkplaceType",
                table: "Jobs");
        }
    }
}
