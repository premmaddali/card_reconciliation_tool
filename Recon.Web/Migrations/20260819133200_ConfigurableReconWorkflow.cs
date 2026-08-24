using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurableReconWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentWorkflowStepId",
                table: "ReconRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextAction",
                table: "ReconRuns",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponsibleActor",
                table: "ReconRuns",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "VerificationStatus",
                table: "ReconRuns",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowDefinitionId",
                table: "ReconRuns",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReconWorkflowActors",
                columns: table => new
                {
                    ReconWorkflowActorId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReconRunId = table.Column<int>(type: "int", nullable: false),
                    ReconWorkflowStepId = table.Column<int>(type: "int", nullable: false),
                    AppAccessId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconWorkflowActors", x => x.ReconWorkflowActorId);
                });

            migrationBuilder.CreateTable(
                name: "ReconWorkflowDefinitions",
                columns: table => new
                {
                    ReconWorkflowDefinitionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconWorkflowDefinitions", x => x.ReconWorkflowDefinitionId);
                });

            migrationBuilder.CreateTable(
                name: "ReconWorkflowHistories",
                columns: table => new
                {
                    ReconWorkflowHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReconRunId = table.Column<int>(type: "int", nullable: false),
                    ReconWorkflowStepId = table.Column<int>(type: "int", nullable: true),
                    AppAccessId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    ActionedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconWorkflowHistories", x => x.ReconWorkflowHistoryId);
                });

            migrationBuilder.CreateTable(
                name: "ReconWorkflowSteps",
                columns: table => new
                {
                    ReconWorkflowStepId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReconWorkflowDefinitionId = table.Column<int>(type: "int", nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconWorkflowSteps", x => x.ReconWorkflowStepId);
                    table.ForeignKey(
                        name: "FK_ReconWorkflowSteps_ReconWorkflowDefinitions_ReconWorkflowDefinitionId",
                        column: x => x.ReconWorkflowDefinitionId,
                        principalTable: "ReconWorkflowDefinitions",
                        principalColumn: "ReconWorkflowDefinitionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReconWorkflowStepRoles",
                columns: table => new
                {
                    ReconWorkflowStepRoleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReconWorkflowStepId = table.Column<int>(type: "int", nullable: false),
                    AppAccessId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconWorkflowStepRoles", x => x.ReconWorkflowStepRoleId);
                    table.ForeignKey(
                        name: "FK_ReconWorkflowStepRoles_AppAccess_AppAccessId",
                        column: x => x.AppAccessId,
                        principalTable: "AppAccess",
                        principalColumn: "AppAccessId");
                    table.ForeignKey(
                        name: "FK_ReconWorkflowStepRoles_ReconWorkflowSteps_ReconWorkflowStepId",
                        column: x => x.ReconWorkflowStepId,
                        principalTable: "ReconWorkflowSteps",
                        principalColumn: "ReconWorkflowStepId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_VerificationStatus_CurrentWorkflowStepId",
                table: "ReconRuns",
                columns: new[] { "VerificationStatus", "CurrentWorkflowStepId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconWorkflowActors_AppAccessId_Status",
                table: "ReconWorkflowActors",
                columns: new[] { "AppAccessId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconWorkflowActors_ReconRunId_ReconWorkflowStepId_AppAccessId",
                table: "ReconWorkflowActors",
                columns: new[] { "ReconRunId", "ReconWorkflowStepId", "AppAccessId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconWorkflowDefinitions_Name",
                table: "ReconWorkflowDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconWorkflowHistories_ReconRunId_ActionedDate",
                table: "ReconWorkflowHistories",
                columns: new[] { "ReconRunId", "ActionedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconWorkflowStepRoles_AppAccessId",
                table: "ReconWorkflowStepRoles",
                column: "AppAccessId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconWorkflowStepRoles_ReconWorkflowStepId_AppAccessId",
                table: "ReconWorkflowStepRoles",
                columns: new[] { "ReconWorkflowStepId", "AppAccessId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconWorkflowSteps_ReconWorkflowDefinitionId_StepOrder",
                table: "ReconWorkflowSteps",
                columns: new[] { "ReconWorkflowDefinitionId", "StepOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconWorkflowActors");

            migrationBuilder.DropTable(
                name: "ReconWorkflowHistories");

            migrationBuilder.DropTable(
                name: "ReconWorkflowStepRoles");

            migrationBuilder.DropTable(
                name: "ReconWorkflowSteps");

            migrationBuilder.DropTable(
                name: "ReconWorkflowDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_ReconRuns_VerificationStatus_CurrentWorkflowStepId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "CurrentWorkflowStepId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "NextAction",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "ResponsibleActor",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "WorkflowDefinitionId",
                table: "ReconRuns");
        }
    }
}
