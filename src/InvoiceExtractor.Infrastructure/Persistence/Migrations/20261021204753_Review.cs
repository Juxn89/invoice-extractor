using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceExtractor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Review : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_invoice",
                table: "documents",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_review_queue",
                table: "documents",
                column: "created_at",
                filter: "status = 'NeedsReview'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documents_review_queue",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "reviewed_invoice",
                table: "documents");
        }
    }
}
