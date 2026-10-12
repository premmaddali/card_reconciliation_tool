using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class InPlaceSecondaryPasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GlStagingId",
                table: "ReconExceptions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ProcessorSource",
                table: "ReconExceptions",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessorStagingId",
                table: "ReconExceptions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReconRunPasses",
                columns: table => new
                {
                    ReconRunPassId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReconRunId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    RunGroup = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Added = table.Column<int>(type: "int", nullable: false),
                    Ambiguous = table.Column<int>(type: "int", nullable: false),
                    TargetRunId = table.Column<int>(type: "int", nullable: true),
                    TargetPeriodKey = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RunAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconRunPasses", x => x.ReconRunPassId);
                    table.ForeignKey(
                        name: "FK_ReconRunPasses_ReconRuns_ReconRunId",
                        column: x => x.ReconRunId,
                        principalTable: "ReconRuns",
                        principalColumn: "ReconRunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReconExceptions_ReconRunId_GlStagingId",
                table: "ReconExceptions",
                columns: new[] { "ReconRunId", "GlStagingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconExceptions_ReconRunId_ProcessorStagingId",
                table: "ReconExceptions",
                columns: new[] { "ReconRunId", "ProcessorStagingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconRunPasses_ReconRunId_Kind",
                table: "ReconRunPasses",
                columns: new[] { "ReconRunId", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconRunPasses");

            migrationBuilder.DropIndex(
                name: "IX_ReconExceptions_ReconRunId_GlStagingId",
                table: "ReconExceptions");

            migrationBuilder.DropIndex(
                name: "IX_ReconExceptions_ReconRunId_ProcessorStagingId",
                table: "ReconExceptions");

            migrationBuilder.DropColumn(
                name: "GlStagingId",
                table: "ReconExceptions");

            migrationBuilder.DropColumn(
                name: "ProcessorSource",
                table: "ReconExceptions");

            migrationBuilder.DropColumn(
                name: "ProcessorStagingId",
                table: "ReconExceptions");
        }
    }
}
