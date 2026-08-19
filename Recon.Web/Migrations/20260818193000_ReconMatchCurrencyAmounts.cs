using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Recon.Web.Data;

#nullable disable

namespace Recon.Web.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260818193000_ReconMatchCurrencyAmounts")]
public partial class ReconMatchCurrencyAmounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(name: "BillingAmount", table: "ReconMatches", type: "decimal(19,4)", nullable: true);
        migrationBuilder.AddColumn<int>(name: "BillingCcy", table: "ReconMatches", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "ProcessorCcy", table: "ReconMatches", type: "int", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "SettlementAmount", table: "ReconMatches", type: "decimal(19,4)", nullable: true);
        migrationBuilder.AddColumn<int>(name: "SettlementCcy", table: "ReconMatches", type: "int", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "BillingAmount", table: "ReconMatches");
        migrationBuilder.DropColumn(name: "BillingCcy", table: "ReconMatches");
        migrationBuilder.DropColumn(name: "ProcessorCcy", table: "ReconMatches");
        migrationBuilder.DropColumn(name: "SettlementAmount", table: "ReconMatches");
        migrationBuilder.DropColumn(name: "SettlementCcy", table: "ReconMatches");
    }
}
