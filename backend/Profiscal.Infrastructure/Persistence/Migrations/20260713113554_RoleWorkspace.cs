using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoleWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ManagerId",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Reclamations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedById = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    AdminNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reclamations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reclamations_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ClientName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DueDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ManagerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsultationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubmitNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTasks_AspNetUsers_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkTasks_AspNetUsers_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskCollaborators",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkTaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskCollaborators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTaskCollaborators_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkTaskCollaborators_WorkTasks_WorkTaskId",
                        column: x => x.WorkTaskId,
                        principalTable: "WorkTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_ManagerId",
                table: "AspNetUsers",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_Reclamations_CreatedById",
                table: "Reclamations",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Reclamations_Status_CreatedAt",
                table: "Reclamations",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskCollaborators_UserId",
                table: "WorkTaskCollaborators",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskCollaborators_WorkTaskId_UserId",
                table: "WorkTaskCollaborators",
                columns: new[] { "WorkTaskId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_ConsultantId_Status",
                table: "WorkTasks",
                columns: new[] { "ConsultantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_ManagerId_CreatedAt",
                table: "WorkTasks",
                columns: new[] { "ManagerId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_ManagerId",
                table: "AspNetUsers",
                column: "ManagerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_ManagerId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Reclamations");

            migrationBuilder.DropTable(
                name: "WorkTaskCollaborators");

            migrationBuilder.DropTable(
                name: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_ManagerId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ManagerId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "AspNetUsers");
        }
    }
}
