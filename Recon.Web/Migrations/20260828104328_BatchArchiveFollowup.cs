using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class BatchArchiveFollowup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "ReconRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BatchArchives",
                columns: table => new
                {
                    BatchArchiveId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OriginalReconRunId = table.Column<int>(type: "int", nullable: false),
                    Function = table.Column<byte>(type: "tinyint", nullable: false),
                    PeriodKey = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    SchemeProfileId = table.Column<int>(type: "int", nullable: true),
                    ArchivedByFreeze = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ArchivedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchArchives", x => x.BatchArchiveId);
                });

            migrationBuilder.CreateTable(
                name: "BatchFreezes",
                columns: table => new
                {
                    BatchFreezeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Function = table.Column<byte>(type: "tinyint", nullable: false),
                    PeriodKey = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    SchemeProfileId = table.Column<int>(type: "int", nullable: true),
                    FrozenFromReconRunId = table.Column<int>(type: "int", nullable: false),
                    FrozenDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FrozenBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchFreezes", x => x.BatchFreezeId);
                });

            migrationBuilder.CreateTable(
                name: "BatchArchiveTransactions",
                columns: table => new
                {
                    BatchArchiveTransactionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchArchiveId = table.Column<int>(type: "int", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MatchStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    ReconciliationAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    GlAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchArchiveTransactions", x => x.BatchArchiveTransactionId);
                    table.ForeignKey(
                        name: "FK_BatchArchiveTransactions_BatchArchives_BatchArchiveId",
                        column: x => x.BatchArchiveId,
                        principalTable: "BatchArchives",
                        principalColumn: "BatchArchiveId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BatchArchives_Function_PeriodKey_SchemeProfileId",
                table: "BatchArchives",
                columns: new[] { "Function", "PeriodKey", "SchemeProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_BatchArchives_OriginalReconRunId",
                table: "BatchArchives",
                column: "OriginalReconRunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BatchArchiveTransactions_BatchArchiveId",
                table: "BatchArchiveTransactions",
                column: "BatchArchiveId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchFreezes_Function_PeriodKey_SchemeProfileId",
                table: "BatchFreezes",
                columns: new[] { "Function", "PeriodKey", "SchemeProfileId" },
                unique: true,
                filter: "[SchemeProfileId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BatchArchiveTransactions");

            migrationBuilder.DropTable(
                name: "BatchFreezes");

            migrationBuilder.DropTable(
                name: "BatchArchives");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "ReconRuns");
        }
    }
}
