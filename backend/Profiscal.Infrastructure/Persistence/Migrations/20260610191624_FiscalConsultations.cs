using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FiscalConsultations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FiscalConsultations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ClientName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Situation = table.Column<string>(type: "TEXT", nullable: false),
                    FiscalQuestion = table.Column<string>(type: "TEXT", nullable: false),
                    ContexteFaits = table.Column<string>(type: "TEXT", nullable: false),
                    Etendue = table.Column<string>(type: "TEXT", nullable: false),
                    Abbreviations = table.Column<string>(type: "TEXT", nullable: false),
                    SommairExecutif = table.Column<string>(type: "TEXT", nullable: false),
                    Analyses = table.Column<string>(type: "TEXT", nullable: false),
                    Documents = table.Column<string>(type: "TEXT", nullable: false),
                    OutputJson = table.Column<string>(type: "TEXT", nullable: false),
                    Branches = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Countries = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IsInternational = table.Column<bool>(type: "INTEGER", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourcesCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ElapsedMs = table.Column<double>(type: "REAL", nullable: false),
                    RefineCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: true),
                    RatingComment = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalConsultations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalConsultations_ClientName",
                table: "FiscalConsultations",
                column: "ClientName");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalConsultations_OwnerUserId_CreatedAt",
                table: "FiscalConsultations",
                columns: new[] { "OwnerUserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiscalConsultations");
        }
    }
}
