using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class MatchSourceDescriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GlDescription",
                table: "ReconMatches",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessorDescription",
                table: "ReconMatches",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessorMerchant",
                table: "ReconMatches",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            // Populate source context for already completed reconciliations as well as future runs.
            migrationBuilder.Sql("""
                UPDATE matchRow
                SET ProcessorMerchant = interchange.MerchantAcronym,
                    ProcessorDescription = interchange.TransactionWording
                FROM ReconMatches AS matchRow
                INNER JOIN StgInterchangeFees AS interchange
                    ON matchRow.ProcessorSource = 2
                   AND matchRow.ProcessorStagingId = interchange.StgInterchangeFeeId
                WHERE matchRow.ProcessorMerchant IS NULL
                   OR matchRow.ProcessorDescription IS NULL;

                UPDATE matchRow
                SET ProcessorMerchant = debit.MerchantAcronym,
                    ProcessorDescription = debit.TransactionWording
                FROM ReconMatches AS matchRow
                INNER JOIN StgDebitSettlements AS debit
                    ON matchRow.ProcessorSource IN (3, 4)
                   AND matchRow.ProcessorStagingId = debit.StgDebitSettlementId
                WHERE matchRow.ProcessorMerchant IS NULL
                   OR matchRow.ProcessorDescription IS NULL;

                UPDATE matchRow
                SET GlDescription = LEFT(NULLIF(CONCAT_WS(N' · ',
                    NULLIF(LTRIM(RTRIM(gl.TranParticular)), N''),
                    NULLIF(LTRIM(RTRIM(gl.TranRmks)), N'')), N''), 512)
                FROM ReconMatches AS matchRow
                INNER JOIN StgGlEntries AS gl ON matchRow.GlStagingId = gl.StgGlEntryId
                WHERE matchRow.GlDescription IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GlDescription",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "ProcessorDescription",
                table: "ReconMatches");

            migrationBuilder.DropColumn(
                name: "ProcessorMerchant",
                table: "ReconMatches");
        }
    }
}
