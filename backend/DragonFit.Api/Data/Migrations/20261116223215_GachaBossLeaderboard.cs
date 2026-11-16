using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DragonFit.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class GachaBossLeaderboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BossKind",
                table: "QuestInstances",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MealDraws",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RollNo = table.Column<int>(type: "integer", nullable: false),
                    TargetKcal = table.Column<int>(type: "integer", nullable: false),
                    TargetProteinG = table.Column<int>(type: "integer", nullable: false),
                    CostGems = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealDraws", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MealDrawItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MealDrawId = table.Column<long>(type: "bigint", nullable: false),
                    FoodId = table.Column<int>(type: "integer", nullable: false),
                    Servings = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    Slot = table.Column<string>(type: "text", nullable: false),
                    Rarity = table.Column<string>(type: "text", nullable: false),
                    MealLogId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealDrawItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealDrawItems_Foods_FoodId",
                        column: x => x.FoodId,
                        principalTable: "Foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MealDrawItems_MealDraws_MealDrawId",
                        column: x => x.MealDrawId,
                        principalTable: "MealDraws",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MealDrawItems_FoodId",
                table: "MealDrawItems",
                column: "FoodId");

            migrationBuilder.CreateIndex(
                name: "IX_MealDrawItems_MealDrawId",
                table: "MealDrawItems",
                column: "MealDrawId");

            migrationBuilder.CreateIndex(
                name: "IX_MealDraws_UserId_Date_RollNo",
                table: "MealDraws",
                columns: new[] { "UserId", "Date", "RollNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealDrawItems");

            migrationBuilder.DropTable(
                name: "MealDraws");

            migrationBuilder.DropColumn(
                name: "BossKind",
                table: "QuestInstances");
        }
    }
}
