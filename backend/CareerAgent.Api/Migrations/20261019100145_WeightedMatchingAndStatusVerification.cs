using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CareerAgent.Api.Migrations
{
    /// <inheritdoc />
    public partial class WeightedMatchingAndStatusVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StatusVerifiedAt",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("UPDATE Jobs SET StatusVerifiedAt = (SELECT MAX(LastSeenAt) FROM JobOccurrences WHERE JobOccurrences.JobId = Jobs.Id AND JobOccurrences.Active = 1) WHERE IsImported = 1 AND IsActive = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusVerifiedAt",
                table: "Jobs");
        }
    }
}
