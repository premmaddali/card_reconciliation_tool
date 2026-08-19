using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class ReconEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReconRuns_ImportBatches_AtmBatchId",
                table: "ReconRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_ReconRuns_ImportBatches_InterchangeBatchId",
                table: "ReconRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_ReconRuns_ImportBatches_NetworkT464BatchId",
                table: "ReconRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_ReconRuns_ImportBatches_PosBatchId",
                table: "ReconRuns");

            migrationBuilder.DropIndex(
                name: "IX_ReconRuns_AtmBatchId",
                table: "ReconRuns");

            migrationBuilder.DropIndex(
                name: "IX_ReconRuns_InterchangeBatchId",
                table: "ReconRuns");

            migrationBuilder.DropIndex(
                name: "IX_ReconRuns_NetworkT464BatchId",
                table: "ReconRuns");

            migrationBuilder.DropIndex(
                name: "IX_ReconRuns_PosBatchId",
                table: "ReconRuns");

            migrationBuilder.DropIndex(
                name: "IX_ReconPassSummaries_ReconRunId_Pass",
                table: "ReconPassSummaries");

            migrationBuilder.DropIndex(
                name: "IX_ReconMatches_ReconRunId_Pass_MatchStatus",
                table: "ReconMatches");

            migrationBuilder.DropIndex(
                name: "IX_ReconExceptions_ReconRunId_Pass_Status",
                table: "ReconExceptions");

            migrationBuilder.DropColumn(
                name: "AtmBatchId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "InterchangeBatchId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "NetworkT464BatchId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "PosBatchId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "Pass",
                table: "ReconPassSummaries");

            migrationBuilder.DropColumn(
                name: "AmountLeft",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "AmountRight",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "LeftSource",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "LeftStagingId",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "RightSource",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "Pass",
                table: "ReconExceptions");

            migrationBuilder.RenameColumn(
                name: "RightStagingId",
                table: "ReconMatches",
                newName: "GlStagingId");

            migrationBuilder.RenameColumn(
                name: "Pass",
                table: "ReconMatches",
                newName: "ProcessorSource");

            migrationBuilder.RenameColumn(
                name: "FeeRight",
                table: "ReconMatches",
                newName: "ProcessorAmount");

            migrationBuilder.RenameColumn(
                name: "FeeLeft",
                table: "ReconMatches",
                newName: "GlAmount");

            migrationBuilder.AlterColumn<int>(
                name: "GlBatchId",
                table: "ReconRuns",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AverageConfidence",
                table: "ReconRuns",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ExceptionCount",
                table: "ReconRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "Function",
                table: "ReconRuns",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "MatchedCount",
                table: "ReconRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProcessorBatchId",
                table: "ReconRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProcessorCount",
                table: "ReconRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "AverageConfidence",
                table: "ReconPassSummaries",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Day",
                table: "ReconPassSummaries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Confidence",
                table: "ReconMatches",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "GlDate",
                table: "ReconMatches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ProcessorDate",
                table: "ReconMatches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessorStagingId",
                table: "ReconMatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ScoreBreakdown",
                table: "ReconMatches",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Confidence",
                table: "ReconExceptions",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TxnDate",
                table: "ReconExceptions",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GlSettlementAccounts",
                columns: table => new
                {
                    GlSettlementAccountId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AcctNum = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Ccy = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<byte>(type: "tinyint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlSettlementAccounts", x => x.GlSettlementAccountId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_Function_PeriodKey",
                table: "ReconRuns",
                columns: new[] { "Function", "PeriodKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_ProcessorBatchId",
                table: "ReconRuns",
                column: "ProcessorBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconPassSummaries_ReconRunId_Ccy_Day",
                table: "ReconPassSummaries",
                columns: new[] { "ReconRunId", "Ccy", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconMatches_ReconRunId_MatchStatus",
                table: "ReconMatches",
                columns: new[] { "ReconRunId", "MatchStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconExceptions_ReconRunId_Status",
                table: "ReconExceptions",
                columns: new[] { "ReconRunId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GlSettlementAccounts_AcctNum",
                table: "GlSettlementAccounts",
                column: "AcctNum",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ReconRuns_ImportBatches_ProcessorBatchId",
                table: "ReconRuns",
                column: "ProcessorBatchId",
                principalTable: "ImportBatches",
                principalColumn: "ImportBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReconRuns_ImportBatches_ProcessorBatchId",
                table: "ReconRuns");

            migrationBuilder.DropTable(
                name: "GlSettlementAccounts");

            migrationBuilder.DropIndex(
                name: "IX_ReconRuns_Function_PeriodKey",
                table: "ReconRuns");

            migrationBuilder.DropIndex(
                name: "IX_ReconRuns_ProcessorBatchId",
                table: "ReconRuns");

            migrationBuilder.DropIndex(
                name: "IX_ReconPassSummaries_ReconRunId_Ccy_Day",
                table: "ReconPassSummaries");

            migrationBuilder.DropIndex(
                name: "IX_ReconMatches_ReconRunId_MatchStatus",
                table: "ReconMatches");

            migrationBuilder.DropIndex(
                name: "IX_ReconExceptions_ReconRunId_Status",
                table: "ReconExceptions");

            migrationBuilder.DropColumn(
                name: "AverageConfidence",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "ExceptionCount",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "Function",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "MatchedCount",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "ProcessorBatchId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "ProcessorCount",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "AverageConfidence",
                table: "ReconPassSummaries");

            migrationBuilder.DropColumn(
                name: "Day",
                table: "ReconPassSummaries");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "GlDate",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "ProcessorDate",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "ProcessorStagingId",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "ScoreBreakdown",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "ReconExceptions");

            migrationBuilder.DropColumn(
                name: "TxnDate",
                table: "ReconExceptions");

            migrationBuilder.RenameColumn(
                name: "ProcessorSource",
                table: "ReconMatches",
                newName: "Pass");

            migrationBuilder.RenameColumn(
                name: "ProcessorAmount",
                table: "ReconMatches",
                newName: "FeeRight");

            migrationBuilder.RenameColumn(
                name: "GlStagingId",
                table: "ReconMatches",
                newName: "RightStagingId");

            migrationBuilder.RenameColumn(
                name: "GlAmount",
                table: "ReconMatches",
                newName: "FeeLeft");

            migrationBuilder.AlterColumn<int>(
                name: "GlBatchId",
                table: "ReconRuns",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "AtmBatchId",
                table: "ReconRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InterchangeBatchId",
                table: "ReconRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NetworkT464BatchId",
                table: "ReconRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PosBatchId",
                table: "ReconRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Pass",
                table: "ReconPassSummaries",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountLeft",
                table: "ReconMatches",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountRight",
                table: "ReconMatches",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "LeftSource",
                table: "ReconMatches",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LeftStagingId",
                table: "ReconMatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "RightSource",
                table: "ReconMatches",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Pass",
                table: "ReconExceptions",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_AtmBatchId",
                table: "ReconRuns",
                column: "AtmBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_InterchangeBatchId",
                table: "ReconRuns",
                column: "InterchangeBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_NetworkT464BatchId",
                table: "ReconRuns",
                column: "NetworkT464BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_PosBatchId",
                table: "ReconRuns",
                column: "PosBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconPassSummaries_ReconRunId_Pass",
                table: "ReconPassSummaries",
                columns: new[] { "ReconRunId", "Pass" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconMatches_ReconRunId_Pass_MatchStatus",
                table: "ReconMatches",
                columns: new[] { "ReconRunId", "Pass", "MatchStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconExceptions_ReconRunId_Pass_Status",
                table: "ReconExceptions",
                columns: new[] { "ReconRunId", "Pass", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_ReconRuns_ImportBatches_AtmBatchId",
                table: "ReconRuns",
                column: "AtmBatchId",
                principalTable: "ImportBatches",
                principalColumn: "ImportBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReconRuns_ImportBatches_InterchangeBatchId",
                table: "ReconRuns",
                column: "InterchangeBatchId",
                principalTable: "ImportBatches",
                principalColumn: "ImportBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReconRuns_ImportBatches_NetworkT464BatchId",
                table: "ReconRuns",
                column: "NetworkT464BatchId",
                principalTable: "ImportBatches",
                principalColumn: "ImportBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReconRuns_ImportBatches_PosBatchId",
                table: "ReconRuns",
                column: "PosBatchId",
                principalTable: "ImportBatches",
                principalColumn: "ImportBatchId");
        }
    }
}
