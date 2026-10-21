using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceExtractor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcessingQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "documents",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "country",
                table: "batches",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: false,
                // Batches uploaded before the country existed (none was processed yet) take the default, Upload:DefaultCountry.
                defaultValue: "NI");

            migrationBuilder.CreateTable(
                name: "extractions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    invoice = table.Column<string>(type: "jsonb", nullable: true),
                    confidence = table.Column<string>(type: "jsonb", nullable: false),
                    issues = table.Column<string>(type: "jsonb", nullable: false),
                    failure = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extractions", x => x.id);
                    table.ForeignKey(
                        name: "fk_extractions_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_extractions_created_at",
                table: "extractions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_extractions_document_id",
                table: "extractions",
                column: "document_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "extractions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "country",
                table: "batches");
        }
    }
}
