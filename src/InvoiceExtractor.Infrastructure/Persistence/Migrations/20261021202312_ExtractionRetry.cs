using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceExtractor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtractionRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_retry",
                table: "extractions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_retry",
                table: "extractions");
        }
    }
}
