using Microsoft.EntityFrameworkCore.Migrations;

namespace MTGViewer.Migrations.Card
{
    public partial class CardInfoChange : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardColor");

            migrationBuilder.DropTable(
                name: "CardSubType");

            migrationBuilder.DropTable(
                name: "CardSuperType");

            migrationBuilder.DropTable(
                name: "CardType");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Type",
                table: "Type");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SuperType",
                table: "SuperType");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubType",
                table: "SubType");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Color",
                table: "Color");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "Type",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0)
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<string>(
                name: "CardId",
                table: "Type",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "SuperType",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0)
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<string>(
                name: "CardId",
                table: "SuperType",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "SubType",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0)
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<string>(
                name: "CardId",
                table: "SubType",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "Color",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0)
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<string>(
                name: "CardId",
                table: "Color",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Type",
                table: "Type",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SuperType",
                table: "SuperType",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubType",
                table: "SubType",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Color",
                table: "Color",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Type_CardId",
                table: "Type",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_SuperType_CardId",
                table: "SuperType",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_SubType_CardId",
                table: "SubType",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_Color_CardId",
                table: "Color",
                column: "CardId");

            migrationBuilder.AddForeignKey(
                name: "FK_Color_Cards_CardId",
                table: "Color",
                column: "CardId",
                principalTable: "Cards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SubType_Cards_CardId",
                table: "SubType",
                column: "CardId",
                principalTable: "Cards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SuperType_Cards_CardId",
                table: "SuperType",
                column: "CardId",
                principalTable: "Cards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Type_Cards_CardId",
                table: "Type",
                column: "CardId",
                principalTable: "Cards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Color_Cards_CardId",
                table: "Color");

            migrationBuilder.DropForeignKey(
                name: "FK_SubType_Cards_CardId",
                table: "SubType");

            migrationBuilder.DropForeignKey(
                name: "FK_SuperType_Cards_CardId",
                table: "SuperType");

            migrationBuilder.DropForeignKey(
                name: "FK_Type_Cards_CardId",
                table: "Type");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Type",
                table: "Type");

            migrationBuilder.DropIndex(
                name: "IX_Type_CardId",
                table: "Type");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SuperType",
                table: "SuperType");

            migrationBuilder.DropIndex(
                name: "IX_SuperType_CardId",
                table: "SuperType");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubType",
                table: "SubType");

            migrationBuilder.DropIndex(
                name: "IX_SubType_CardId",
                table: "SubType");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Color",
                table: "Color");

            migrationBuilder.DropIndex(
                name: "IX_Color_CardId",
                table: "Color");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "Type");

            migrationBuilder.DropColumn(
                name: "CardId",
                table: "Type");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "SuperType");

            migrationBuilder.DropColumn(
                name: "CardId",
                table: "SuperType");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "SubType");

            migrationBuilder.DropColumn(
                name: "CardId",
                table: "SubType");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "Color");

            migrationBuilder.DropColumn(
                name: "CardId",
                table: "Color");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Type",
                table: "Type",
                column: "Name");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SuperType",
                table: "SuperType",
                column: "Name");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubType",
                table: "SubType",
                column: "Name");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Color",
                table: "Color",
                column: "Name");

            migrationBuilder.CreateTable(
                name: "CardColor",
                columns: table => new
                {
                    CardsId = table.Column<string>(type: "TEXT", nullable: false),
                    ColorsName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardColor", x => new { x.CardsId, x.ColorsName });
                    table.ForeignKey(
                        name: "FK_CardColor_Cards_CardsId",
                        column: x => x.CardsId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardColor_Color_ColorsName",
                        column: x => x.ColorsName,
                        principalTable: "Color",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CardSubType",
                columns: table => new
                {
                    CardsId = table.Column<string>(type: "TEXT", nullable: false),
                    SubTypesName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardSubType", x => new { x.CardsId, x.SubTypesName });
                    table.ForeignKey(
                        name: "FK_CardSubType_Cards_CardsId",
                        column: x => x.CardsId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardSubType_SubType_SubTypesName",
                        column: x => x.SubTypesName,
                        principalTable: "SubType",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CardSuperType",
                columns: table => new
                {
                    CardsId = table.Column<string>(type: "TEXT", nullable: false),
                    SuperTypesName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardSuperType", x => new { x.CardsId, x.SuperTypesName });
                    table.ForeignKey(
                        name: "FK_CardSuperType_Cards_CardsId",
                        column: x => x.CardsId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardSuperType_SuperType_SuperTypesName",
                        column: x => x.SuperTypesName,
                        principalTable: "SuperType",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CardType",
                columns: table => new
                {
                    CardsId = table.Column<string>(type: "TEXT", nullable: false),
                    TypesName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardType", x => new { x.CardsId, x.TypesName });
                    table.ForeignKey(
                        name: "FK_CardType_Cards_CardsId",
                        column: x => x.CardsId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardType_Type_TypesName",
                        column: x => x.TypesName,
                        principalTable: "Type",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardColor_ColorsName",
                table: "CardColor",
                column: "ColorsName");

            migrationBuilder.CreateIndex(
                name: "IX_CardSubType_SubTypesName",
                table: "CardSubType",
                column: "SubTypesName");

            migrationBuilder.CreateIndex(
                name: "IX_CardSuperType_SuperTypesName",
                table: "CardSuperType",
                column: "SuperTypesName");

            migrationBuilder.CreateIndex(
                name: "IX_CardType_TypesName",
                table: "CardType",
                column: "TypesName");
        }
    }
}
