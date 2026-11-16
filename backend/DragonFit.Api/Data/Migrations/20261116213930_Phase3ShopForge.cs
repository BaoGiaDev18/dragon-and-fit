using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DragonFit.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3ShopForge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "ShopItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ShopItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ShopItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CustomSpriteKey",
                table: "Characters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RestPassDate",
                table: "Characters",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Purchases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ShopItemId = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<int>(type: "integer", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedForDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Purchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Purchases_ShopItems_ShopItemId",
                        column: x => x.ShopItemId,
                        principalTable: "ShopItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_Code",
                table: "ShopItems",
                column: "Code",
                unique: true,
                filter: "\"Code\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_ShopItemId",
                table: "Purchases",
                column: "ShopItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_UserId_ShopItemId",
                table: "Purchases",
                columns: new[] { "UserId", "ShopItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_ShopItems_Code",
                table: "ShopItems");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "ShopItems");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ShopItems");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ShopItems");

            migrationBuilder.DropColumn(
                name: "CustomSpriteKey",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "RestPassDate",
                table: "Characters");
        }
    }
}
