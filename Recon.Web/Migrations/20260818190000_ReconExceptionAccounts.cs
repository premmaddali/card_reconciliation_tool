using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Recon.Web.Data;

#nullable disable

namespace Recon.Web.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260818190000_ReconExceptionAccounts")]
public partial class ReconExceptionAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "GlAccountNumber", table: "ReconExceptions", type: "nvarchar(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ProcessorAccountNumber", table: "ReconExceptions", type: "nvarchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "GlAccountNumber", table: "ReconMatches", type: "nvarchar(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ProcessorAccountNumber", table: "ReconMatches", type: "nvarchar(64)", maxLength: 64, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "GlAccountNumber", table: "ReconExceptions");
        migrationBuilder.DropColumn(name: "ProcessorAccountNumber", table: "ReconExceptions");
        migrationBuilder.DropColumn(name: "GlAccountNumber", table: "ReconMatches");
        migrationBuilder.DropColumn(name: "ProcessorAccountNumber", table: "ReconMatches");
    }
}
