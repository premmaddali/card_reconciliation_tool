using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Recon.Web.Data;

#nullable disable

namespace Recon.Web.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260825091000_ReconMatchMicrofilmReference")]
public partial class ReconMatchMicrofilmReference : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "MicrofilmRefNumber", table: "ReconMatches", type: "nvarchar(32)", maxLength: 32, nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "MicrofilmRefNumber", table: "ReconMatches");
}
