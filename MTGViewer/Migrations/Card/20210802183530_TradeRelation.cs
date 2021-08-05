using Microsoft.EntityFrameworkCore.Migrations;

namespace MTGViewer.Migrations.Card
{
    public partial class TradeRelation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trades_FromId",
                table: "Trades");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_FromId",
                table: "Trades",
                column: "FromId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trades_FromId",
                table: "Trades");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_FromId",
                table: "Trades",
                column: "FromId",
                unique: true);
        }
    }
}
