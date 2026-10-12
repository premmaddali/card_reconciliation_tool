using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class DailyPeriodRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentPeriodRunId",
                table: "ReconRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "PeriodGranularity",
                table: "ReconRuns",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "SettlementSpreadDays",
                table: "ReconParameters",
                type: "int",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.CreateTable(
                name: "ReconPeriodRuns",
                columns: table => new
                {
                    ReconPeriodRunId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Function = table.Column<byte>(type: "tinyint", nullable: false),
                    PeriodKey = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    SchemeProfileId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    RolledUpRunId = table.Column<int>(type: "int", nullable: true),
                    RolledUpDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconPeriodRuns", x => x.ReconPeriodRunId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_ParentPeriodRunId",
                table: "ReconRuns",
                column: "ParentPeriodRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconPeriodRuns_Function_PeriodKey_SchemeProfileId",
                table: "ReconPeriodRuns",
                columns: new[] { "Function", "PeriodKey", "SchemeProfileId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconPeriodRuns");

            migrationBuilder.DropIndex(
                name: "IX_ReconRuns_ParentPeriodRunId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "ParentPeriodRunId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "PeriodGranularity",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "SettlementSpreadDays",
                table: "ReconParameters");
        }
    }
}
