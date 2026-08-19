using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Recon.Web.Data;

#nullable disable

namespace Recon.Web.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260818183000_FxReconciliationAmount")]
public partial class FxReconciliationAmount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ReconciliationAmount",
            table: "ReconMatches",
            type: "decimal(19,4)",
            nullable: true);

        // Historical matches were transaction-currency matches. Preserve their existing amount as
        // the reconciliation value, so current summaries and newly added FX-aware summaries agree.
        migrationBuilder.Sql("UPDATE ReconMatches SET ReconciliationAmount = ProcessorAmount WHERE ReconciliationAmount IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReconciliationAmount",
            table: "ReconMatches");
    }
}
