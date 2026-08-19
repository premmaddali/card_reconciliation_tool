using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    ImportBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<byte>(type: "tinyint", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PeriodKey = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ParseError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.ImportBatchId);
                });

            migrationBuilder.CreateTable(
                name: "StgDebitSettlements",
                columns: table => new
                {
                    StgDebitSettlementId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportBatchId = table.Column<int>(type: "int", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<byte>(type: "tinyint", nullable: false),
                    ExtractedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MerchantAcronym = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcessingCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    CardNumberHashed = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CardNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Wording = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProductType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TransactionSign = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TransactionWording = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ProcessingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TxnAmountRaw = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TxnAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    TxnCcy = table.Column<int>(type: "int", nullable: true),
                    TxnCcyCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InterchangeFeesRaw = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InterchangeFees = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    IntFeesCcy = table.Column<int>(type: "int", nullable: true),
                    IntFeesCcyCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BillingAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    BillingCcy = table.Column<int>(type: "int", nullable: true),
                    ConversionFees = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    SettlementAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    SettlementCcy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StgDebitSettlements", x => x.StgDebitSettlementId);
                });

            migrationBuilder.CreateTable(
                name: "StgGlEntries",
                columns: table => new
                {
                    StgGlEntryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportBatchId = table.Column<int>(type: "int", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    SolId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AcctNum = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_StgGlEntries", x => x.StgGlEntryId);
                });

            migrationBuilder.CreateTable(
                name: "StgInterchangeFees",
                columns: table => new
                {
                    StgInterchangeFeeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportBatchId = table.Column<int>(type: "int", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    MicrofilmRefNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MicrofilmRefSeq = table.Column<int>(type: "int", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    CardNumberHashed = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProductCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProductType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CardAccountNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TransactionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TransactionCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TransactionWording = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TxnAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    TxnCcy = table.Column<int>(type: "int", nullable: true),
                    BillingAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    BillingCcy = table.Column<int>(type: "int", nullable: true),
                    SettlementAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    SettlementCcy = table.Column<int>(type: "int", nullable: true),
                    InterchangeFeesRaw = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InterchangeFees = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    IntFeesCcy = table.Column<int>(type: "int", nullable: true),
                    OrigineCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TerminalId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MerchantAcronym = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IssMarkupAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    ConversionFees = table.Column<decimal>(type: "decimal(19,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StgInterchangeFees", x => x.StgInterchangeFeeId);
                });

            migrationBuilder.CreateTable(
                name: "StgNetworkT464",
                columns: table => new
                {
                    StgNetworkT464Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportBatchId = table.Column<int>(type: "int", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    HeaderCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MessageTypeIndicator = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SwitchSerialNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcessorId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TransactionTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    Pan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcessingCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TraceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MerchantType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    AcquirerInstitutionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TerminalId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TxnCcy = table.Column<int>(type: "int", nullable: true),
                    TxnCcyCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletedAmtLocalRaw = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletedAmtLocal = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    SettleCcy = table.Column<int>(type: "int", nullable: true),
                    SettleCcyCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConversionRate = table.Column<decimal>(type: "decimal(19,8)", nullable: true),
                    CompletedAmtSettleRaw = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletedAmtSettle = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    InterchangeFeeRaw = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InterchangeFee = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    RequestedAmtLocal = table.Column<decimal>(type: "decimal(19,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StgNetworkT464", x => x.StgNetworkT464Id);
                });

            migrationBuilder.CreateTable(
                name: "ReconRuns",
                columns: table => new
                {
                    ReconRunId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PeriodKey = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    NetworkT464BatchId = table.Column<int>(type: "int", nullable: true),
                    InterchangeBatchId = table.Column<int>(type: "int", nullable: true),
                    PosBatchId = table.Column<int>(type: "int", nullable: true),
                    AtmBatchId = table.Column<int>(type: "int", nullable: true),
                    GlBatchId = table.Column<int>(type: "int", nullable: true),
                    StartedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconRuns", x => x.ReconRunId);
                    table.ForeignKey(
                        name: "FK_ReconRuns_ImportBatches_AtmBatchId",
                        column: x => x.AtmBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "ImportBatchId");
                    table.ForeignKey(
                        name: "FK_ReconRuns_ImportBatches_GlBatchId",
                        column: x => x.GlBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "ImportBatchId");
                    table.ForeignKey(
                        name: "FK_ReconRuns_ImportBatches_InterchangeBatchId",
                        column: x => x.InterchangeBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "ImportBatchId");
                    table.ForeignKey(
                        name: "FK_ReconRuns_ImportBatches_NetworkT464BatchId",
                        column: x => x.NetworkT464BatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "ImportBatchId");
                    table.ForeignKey(
                        name: "FK_ReconRuns_ImportBatches_PosBatchId",
                        column: x => x.PosBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "ImportBatchId");
                });

            migrationBuilder.CreateTable(
                name: "ReconExceptions",
                columns: table => new
                {
                    ReconExceptionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReconRunId = table.Column<int>(type: "int", nullable: false),
                    Pass = table.Column<byte>(type: "tinyint", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    ExceptionType = table.Column<byte>(type: "tinyint", nullable: false),
                    Ccy = table.Column<int>(type: "int", nullable: true),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    ActualAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    Difference = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    Narrative = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ResolvedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconExceptions", x => x.ReconExceptionId);
                    table.ForeignKey(
                        name: "FK_ReconExceptions_ReconRuns_ReconRunId",
                        column: x => x.ReconRunId,
                        principalTable: "ReconRuns",
                        principalColumn: "ReconRunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReconMatches",
                columns: table => new
                {
                    ReconMatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReconRunId = table.Column<int>(type: "int", nullable: false),
                    Pass = table.Column<byte>(type: "tinyint", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    LeftStagingId = table.Column<int>(type: "int", nullable: true),
                    LeftSource = table.Column<byte>(type: "tinyint", nullable: true),
                    RightStagingId = table.Column<int>(type: "int", nullable: true),
                    RightSource = table.Column<byte>(type: "tinyint", nullable: true),
                    AmountLeft = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    AmountRight = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    FeeLeft = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    FeeRight = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    Ccy = table.Column<int>(type: "int", nullable: true),
                    MatchStatus = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconMatches", x => x.ReconMatchId);
                    table.ForeignKey(
                        name: "FK_ReconMatches_ReconRuns_ReconRunId",
                        column: x => x.ReconRunId,
                        principalTable: "ReconRuns",
                        principalColumn: "ReconRunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReconPassSummaries",
                columns: table => new
                {
                    ReconPassSummaryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReconRunId = table.Column<int>(type: "int", nullable: false),
                    Pass = table.Column<byte>(type: "tinyint", nullable: false),
                    Ccy = table.Column<int>(type: "int", nullable: false),
                    MatchedCount = table.Column<int>(type: "int", nullable: false),
                    MatchedAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    ExceptionCount = table.Column<int>(type: "int", nullable: false),
                    ExceptionAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconPassSummaries", x => x.ReconPassSummaryId);
                    table.ForeignKey(
                        name: "FK_ReconPassSummaries_ReconRuns_ReconRunId",
                        column: x => x.ReconRunId,
                        principalTable: "ReconRuns",
                        principalColumn: "ReconRunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReconExceptionComments",
                columns: table => new
                {
                    ReconExceptionCommentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReconExceptionId = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconExceptionComments", x => x.ReconExceptionCommentId);
                    table.ForeignKey(
                        name: "FK_ReconExceptionComments_ReconExceptions_ReconExceptionId",
                        column: x => x.ReconExceptionId,
                        principalTable: "ReconExceptions",
                        principalColumn: "ReconExceptionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_SourceType_ContentHash",
                table: "ImportBatches",
                columns: new[] { "SourceType", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_SourceType_PeriodKey",
                table: "ImportBatches",
                columns: new[] { "SourceType", "PeriodKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconExceptionComments_ReconExceptionId",
                table: "ReconExceptionComments",
                column: "ReconExceptionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconExceptions_ReconRunId_Pass_Status",
                table: "ReconExceptions",
                columns: new[] { "ReconRunId", "Pass", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconMatches_ReconRunId_Pass_MatchStatus",
                table: "ReconMatches",
                columns: new[] { "ReconRunId", "Pass", "MatchStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconPassSummaries_ReconRunId_Pass",
                table: "ReconPassSummaries",
                columns: new[] { "ReconRunId", "Pass" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_AtmBatchId",
                table: "ReconRuns",
                column: "AtmBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_GlBatchId",
                table: "ReconRuns",
                column: "GlBatchId");

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
                name: "IX_StgDebitSettlements_Channel_ReferenceNumber",
                table: "StgDebitSettlements",
                columns: new[] { "Channel", "ReferenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_StgDebitSettlements_ImportBatchId",
                table: "StgDebitSettlements",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StgDebitSettlements_ReferenceNumber",
                table: "StgDebitSettlements",
                column: "ReferenceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_StgGlEntries_GlSubHeadCode",
                table: "StgGlEntries",
                column: "GlSubHeadCode");

            migrationBuilder.CreateIndex(
                name: "IX_StgGlEntries_ImportBatchId",
                table: "StgGlEntries",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StgGlEntries_TranDate",
                table: "StgGlEntries",
                column: "TranDate");

            migrationBuilder.CreateIndex(
                name: "IX_StgInterchangeFees_ImportBatchId",
                table: "StgInterchangeFees",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StgInterchangeFees_ReferenceNumber",
                table: "StgInterchangeFees",
                column: "ReferenceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_StgNetworkT464_ImportBatchId",
                table: "StgNetworkT464",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StgNetworkT464_ReferenceNumber",
                table: "StgNetworkT464",
                column: "ReferenceNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconExceptionComments");

            migrationBuilder.DropTable(
                name: "ReconMatches");

            migrationBuilder.DropTable(
                name: "ReconPassSummaries");

            migrationBuilder.DropTable(
                name: "StgDebitSettlements");

            migrationBuilder.DropTable(
                name: "StgGlEntries");

            migrationBuilder.DropTable(
                name: "StgInterchangeFees");

            migrationBuilder.DropTable(
                name: "StgNetworkT464");

            migrationBuilder.DropTable(
                name: "ReconExceptions");

            migrationBuilder.DropTable(
                name: "ReconRuns");

            migrationBuilder.DropTable(
                name: "ImportBatches");
        }
    }
}
