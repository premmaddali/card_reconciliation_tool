using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class SplitGlBulkPostings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StgGlBulkPostings",
                columns: table => new
                {
                    StgGlBulkPostingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportBatchId = table.Column<int>(type: "int", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    SolId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AcctNum = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    GlSubHeadCode = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    GlAccount = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AcctName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TranDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TranId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefNum = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TranParticular = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TranParticular2 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TranParticularCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Debit = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    Credit = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    TranRmks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TranAmt = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    TranCcyCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EntryUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VfdUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EntryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VfdDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Rate = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    PartTranSrlNum = table.Column<int>(type: "int", nullable: true),
                    MurAmt = table.Column<decimal>(type: "decimal(19,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StgGlBulkPostings", x => x.StgGlBulkPostingId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StgGlBulkPostings_AcctNum",
                table: "StgGlBulkPostings",
                column: "AcctNum");

            migrationBuilder.CreateIndex(
                name: "IX_StgGlBulkPostings_GlSubHeadCode",
                table: "StgGlBulkPostings",
                column: "GlSubHeadCode");

            migrationBuilder.CreateIndex(
                name: "IX_StgGlBulkPostings_ImportBatchId",
                table: "StgGlBulkPostings",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StgGlBulkPostings_TranDate",
                table: "StgGlBulkPostings",
                column: "TranDate");

            migrationBuilder.Sql("""
                INSERT INTO StgGlBulkPostings (
                    ImportBatchId, SourceRowNumber, SolId, AcctNum, GlSubHeadCode, GlAccount, AcctName,
                    TranDate, TranId, RefNum, TranParticular, TranParticular2, TranParticularCode,
                    Debit, Credit, TranRmks, TranAmt, TranCcyCode, EntryUserId, VfdUserId,
                    EntryDate, VfdDate, Rate, PartTranSrlNum, MurAmt)
                SELECT
                    ImportBatchId, SourceRowNumber, SolId, AcctNum, GlSubHeadCode, GlAccount, AcctName,
                    TranDate, TranId, RefNum, TranParticular, TranParticular2, TranParticularCode,
                    Debit, Credit, TranRmks, TranAmt, TranCcyCode, EntryUserId, VfdUserId,
                    EntryDate, VfdDate, Rate, PartTranSrlNum, MurAmt
                FROM StgGlEntries
                WHERE
                    UPPER(CONCAT(COALESCE(TranParticular, ''), ' ', COALESCE(TranParticular2, ''), ' ', COALESCE(TranRmks, ''))) LIKE '%DEBIT CARD SETTLEMENT%'
                    OR UPPER(CONCAT(COALESCE(TranParticular, ''), ' ', COALESCE(TranParticular2, ''), ' ', COALESCE(TranRmks, ''))) LIKE '%MC SETTLEMENT%'
                    OR UPPER(CONCAT(COALESCE(TranParticular, ''), ' ', COALESCE(TranParticular2, ''), ' ', COALESCE(TranRmks, ''))) LIKE '%CARD SETTLEMENT%'
                    OR UPPER(CONCAT(COALESCE(TranParticular, ''), ' ', COALESCE(TranParticular2, ''), ' ', COALESCE(TranRmks, ''))) LIKE '%SETTLEMENT DD%';

                DELETE FROM StgGlEntries
                WHERE
                    UPPER(CONCAT(COALESCE(TranParticular, ''), ' ', COALESCE(TranParticular2, ''), ' ', COALESCE(TranRmks, ''))) LIKE '%DEBIT CARD SETTLEMENT%'
                    OR UPPER(CONCAT(COALESCE(TranParticular, ''), ' ', COALESCE(TranParticular2, ''), ' ', COALESCE(TranRmks, ''))) LIKE '%MC SETTLEMENT%'
                    OR UPPER(CONCAT(COALESCE(TranParticular, ''), ' ', COALESCE(TranParticular2, ''), ' ', COALESCE(TranRmks, ''))) LIKE '%CARD SETTLEMENT%'
                    OR UPPER(CONCAT(COALESCE(TranParticular, ''), ' ', COALESCE(TranParticular2, ''), ' ', COALESCE(TranRmks, ''))) LIKE '%SETTLEMENT DD%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO StgGlEntries (
                    ImportBatchId, SourceRowNumber, SolId, AcctNum, GlSubHeadCode, GlAccount, AcctName,
                    TranDate, TranId, RefNum, TranParticular, TranParticular2, TranParticularCode,
                    Debit, Credit, TranRmks, TranAmt, TranCcyCode, EntryUserId, VfdUserId,
                    EntryDate, VfdDate, Rate, PartTranSrlNum, MurAmt)
                SELECT
                    ImportBatchId, SourceRowNumber, SolId, AcctNum, GlSubHeadCode, GlAccount, AcctName,
                    TranDate, TranId, RefNum, TranParticular, TranParticular2, TranParticularCode,
                    Debit, Credit, TranRmks, TranAmt, TranCcyCode, EntryUserId, VfdUserId,
                    EntryDate, VfdDate, Rate, PartTranSrlNum, MurAmt
                FROM StgGlBulkPostings;
                """);

            migrationBuilder.DropTable(
                name: "StgGlBulkPostings");
        }
    }
}
