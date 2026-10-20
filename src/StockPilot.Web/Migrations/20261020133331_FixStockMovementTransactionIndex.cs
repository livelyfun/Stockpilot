using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockPilot.Web.Migrations
{
    /// <inheritdoc />
    public partial class FixStockMovementTransactionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_One_Per_Transaction",
                table: "StockMovements");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_One_Per_Transaction",
                table: "StockMovements",
                columns: new[] { "MovementType", "ReferenceId", "ProductId" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND \"MovementType\" IN ('PurchaseReceipt','SaleIssue','SaleReversal','PurchaseReversal')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_One_Per_Transaction",
                table: "StockMovements");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_One_Per_Transaction",
                table: "StockMovements",
                columns: new[] { "MovementType", "ReferenceId" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND \"MovementType\" IN ('PurchaseReceipt','SaleIssue','SaleReversal','PurchaseReversal')");
        }
    }
}
