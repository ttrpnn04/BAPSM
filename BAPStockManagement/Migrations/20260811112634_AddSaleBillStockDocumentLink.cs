using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BAPStockManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleBillStockDocumentLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "StockDocumentID",
                table: "SaleBills",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UQ_SaleBills_StockDocumentID",
                table: "SaleBills",
                column: "StockDocumentID",
                unique: true,
                filter: "[StockDocumentID] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SaleBills_StockDocuments",
                table: "SaleBills",
                column: "StockDocumentID",
                principalTable: "StockDocuments",
                principalColumn: "DocumentID",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SaleBills_StockDocuments",
                table: "SaleBills");

            migrationBuilder.DropIndex(
                name: "UQ_SaleBills_StockDocumentID",
                table: "SaleBills");

            migrationBuilder.DropColumn(
                name: "StockDocumentID",
                table: "SaleBills");
        }
    }
}
