using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MTGViewer.Migrations.Card
{
    public partial class AddCards : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Layout = table.Column<string>(type: "TEXT", nullable: true),
                    ManaCost = table.Column<string>(type: "TEXT", nullable: true),
                    Cmc = table.Column<int>(type: "INTEGER", nullable: true),
                    Rarity = table.Column<string>(type: "TEXT", nullable: true),
                    SetName = table.Column<string>(type: "TEXT", nullable: true),
                    Artist = table.Column<string>(type: "TEXT", nullable: true),
                    Text = table.Column<string>(type: "TEXT", nullable: true),
                    Flavor = table.Column<string>(type: "TEXT", nullable: true),
                    Power = table.Column<string>(type: "TEXT", nullable: true),
                    Toughness = table.Column<string>(type: "TEXT", nullable: true),
                    Loyalty = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Color",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Color", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: true),
                    LiteToken = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Locations_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubType",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubType", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "SuperType",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuperType", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "Type",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Type", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "Name",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    CardId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Name", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Name_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "Amounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardId = table.Column<string>(type: "TEXT", nullable: false),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRequest = table.Column<bool>(type: "INTEGER", nullable: false),
                    Amount = table.Column<int>(type: "INTEGER", nullable: false),
                    LiteToken = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Amounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Amounts_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Amounts_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
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

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardId = table.Column<string>(type: "TEXT", nullable: false),
                    ProposerId = table.Column<string>(type: "TEXT", nullable: false),
                    ReceiverId = table.Column<string>(type: "TEXT", nullable: false),
                    IsCounter = table.Column<bool>(type: "INTEGER", nullable: false),
                    FromId = table.Column<int>(type: "INTEGER", nullable: true),
                    ToId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<int>(type: "INTEGER", nullable: false),
                    LiteToken = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trades_Amounts_FromId",
                        column: x => x.FromId,
                        principalTable: "Amounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Trades_AspNetUsers_ProposerId",
                        column: x => x.ProposerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Trades_AspNetUsers_ReceiverId",
                        column: x => x.ReceiverId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Trades_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Trades_Locations_ToId",
                        column: x => x.ToId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Amounts_CardId",
                table: "Amounts",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_Amounts_LocationId_CardId_IsRequest",
                table: "Amounts",
                columns: new[] { "LocationId", "CardId", "IsRequest" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_Locations_OwnerId",
                table: "Locations",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Name_CardId",
                table: "Name",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_CardId_ProposerId_ReceiverId_ToId_FromId",
                table: "Trades",
                columns: new[] { "CardId", "ProposerId", "ReceiverId", "ToId", "FromId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trades_FromId",
                table: "Trades",
                column: "FromId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trades_ProposerId",
                table: "Trades",
                column: "ProposerId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_ReceiverId",
                table: "Trades",
                column: "ReceiverId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_ToId",
                table: "Trades",
                column: "ToId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardColor");

            migrationBuilder.DropTable(
                name: "CardSubType");

            migrationBuilder.DropTable(
                name: "CardSuperType");

            migrationBuilder.DropTable(
                name: "CardType");

            migrationBuilder.DropTable(
                name: "Name");

            migrationBuilder.DropTable(
                name: "Trades");

            migrationBuilder.DropTable(
                name: "Color");

            migrationBuilder.DropTable(
                name: "SubType");

            migrationBuilder.DropTable(
                name: "SuperType");

            migrationBuilder.DropTable(
                name: "Type");

            migrationBuilder.DropTable(
                name: "Amounts");

            migrationBuilder.DropTable(
                name: "Cards");

            migrationBuilder.DropTable(
                name: "Locations");
        }
    }
}
