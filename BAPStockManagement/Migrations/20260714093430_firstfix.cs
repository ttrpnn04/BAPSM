using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BAPStockManagement.Migrations
{
    /// <inheritdoc />
    public partial class firstfix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("Relational:DefaultConstraintName", "DF_Products_SortOrder");

            migrationBuilder.CreateTable(
                name: "StockDocuments",
                columns: table => new
                {
                    DocumentID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionTypeID = table.Column<int>(type: "int", nullable: false),
                    TxnDate = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "(CONVERT([date],sysdatetime()))")
                        .Annotation("Relational:DefaultConstraintName", "DF_StockDocuments_TxnDate"),
                    RefNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                        .Annotation("Relational:DefaultConstraintName", "DF_StockDocuments_CreatedAt"),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockDocuments", x => x.DocumentID);
                    table.ForeignKey(
                        name: "FK_StockDocuments_Types",
                        column: x => x.TransactionTypeID,
                        principalTable: "TransactionTypes",
                        principalColumn: "TransactionTypeID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransactions_DocumentID",
                table: "StockTransactions",
                column: "DocumentID");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_Date",
                table: "StockDocuments",
                column: "TxnDate");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_RefNo",
                table: "StockDocuments",
                column: "RefNo");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_TransactionTypeID",
                table: "StockDocuments",
                column: "TransactionTypeID");

            migrationBuilder.AddForeignKey(
                name: "FK_StockTransactions_Documents",
                table: "StockTransactions",
                column: "DocumentID",
                principalTable: "StockDocuments",
                principalColumn: "DocumentID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockTransactions_Documents",
                table: "StockTransactions");

            migrationBuilder.DropTable(
                name: "StockDocuments");

            migrationBuilder.DropIndex(
                name: "IX_StockTransactions_DocumentID",
                table: "StockTransactions");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Products")
                .Annotation("Relational:DefaultConstraintName", "DF_Products_SortOrder");
        }
    }
}