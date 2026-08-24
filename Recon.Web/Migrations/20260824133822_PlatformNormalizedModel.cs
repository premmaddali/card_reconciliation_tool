using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class PlatformNormalizedModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NormalizedGlEntries",
                columns: table => new
                {
                    NormalizedGlEntryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportBatchId = table.Column<int>(type: "int", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    SourceTable = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SourceStagingId = table.Column<int>(type: "int", nullable: true),
                    Account = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PostingDateTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DebitCreditIndicator = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    IsCash = table.Column<bool>(type: "bit", nullable: false),
                    IsPos = table.Column<bool>(type: "bit", nullable: false),
                    SettlementLeg = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedGlEntries", x => x.NormalizedGlEntryId);
                });

            migrationBuilder.CreateTable(
                name: "NormalizedTransactions",
                columns: table => new
                {
                    NormalizedTransactionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportBatchId = table.Column<int>(type: "int", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    SourceTable = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SourceStagingId = table.Column<int>(type: "int", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExternalReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AuthorizationCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    TraceNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    BatchNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TransactionDateTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<int>(type: "int", nullable: true),
                    BillingAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    BillingCurrency = table.Column<int>(type: "int", nullable: true),
                    SettlementAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    SettlementCurrency = table.Column<int>(type: "int", nullable: true),
                    DebitCreditIndicator = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Merchant = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Terminal = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Acquirer = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Issuer = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    MaskedCardNumber = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    CardType = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    Scheme = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Processor = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Account = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TransactionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsCash = table.Column<bool>(type: "bit", nullable: false),
                    IsPos = table.Column<bool>(type: "bit", nullable: false),
                    SettlementLeg = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedTransactions", x => x.NormalizedTransactionId);
                });

            migrationBuilder.CreateTable(
                name: "SchemeProfiles",
                columns: table => new
                {
                    SchemeProfileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Scheme = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Cms = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TransactionAdapterCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GlcAdapterCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MatchThresholdOverride = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    DateToleranceDaysOverride = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchemeProfiles", x => x.SchemeProfileId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedGlEntries_ImportBatchId_Account",
                table: "NormalizedGlEntries",
                columns: new[] { "ImportBatchId", "Account" });

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedGlEntries_ImportBatchId_Reference",
                table: "NormalizedGlEntries",
                columns: new[] { "ImportBatchId", "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedTransactions_ImportBatchId_Currency_Amount",
                table: "NormalizedTransactions",
                columns: new[] { "ImportBatchId", "Currency", "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedTransactions_ImportBatchId_Reference",
                table: "NormalizedTransactions",
                columns: new[] { "ImportBatchId", "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_SchemeProfiles_Scheme_Cms",
                table: "SchemeProfiles",
                columns: new[] { "Scheme", "Cms" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NormalizedGlEntries");

            migrationBuilder.DropTable(
                name: "NormalizedTransactions");

            migrationBuilder.DropTable(
                name: "SchemeProfiles");
        }
    }
}
