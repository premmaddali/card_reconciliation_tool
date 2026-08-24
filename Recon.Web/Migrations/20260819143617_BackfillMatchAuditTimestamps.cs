using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class BackfillMatchAuditTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing interchange runs retain their staging-row IDs, so populate the new audit
            // fields without requiring users to re-run a completed reconciliation.
            migrationBuilder.Sql("""
                UPDATE matchRow
                SET ProcessorProductType = interchange.ProductType,
                    ProcessorTransactionDateTime = interchange.TransactionDate
                FROM ReconMatches AS matchRow
                INNER JOIN StgInterchangeFees AS interchange
                    ON matchRow.ProcessorSource = 2
                   AND matchRow.ProcessorStagingId = interchange.StgInterchangeFeeId
                WHERE matchRow.ProcessorProductType IS NULL
                   OR matchRow.ProcessorTransactionDateTime IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE matchRow
                SET GlPostingDateTime = COALESCE(gl.EntryDate, gl.VfdDate)
                FROM ReconMatches AS matchRow
                INNER JOIN StgGlEntries AS gl
                    ON matchRow.GlStagingId = gl.StgGlEntryId
                WHERE matchRow.GlPostingDateTime IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
