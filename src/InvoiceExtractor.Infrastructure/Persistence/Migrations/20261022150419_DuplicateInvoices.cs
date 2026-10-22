using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceExtractor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DuplicateInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "duplicate_of",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invoice_key",
                table: "documents",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_duplicate_of",
                table: "documents",
                column: "duplicate_of");

            migrationBuilder.CreateIndex(
                name: "ix_documents_invoice_key",
                table: "documents",
                column: "invoice_key",
                unique: true,
                filter: "invoice_key IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_documents_documents_duplicate_of",
                table: "documents",
                column: "duplicate_of",
                principalTable: "documents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_documents_documents_duplicate_of",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ix_documents_duplicate_of",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ix_documents_invoice_key",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "duplicate_of",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "invoice_key",
                table: "documents");
        }
    }
}
