using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DragonFit.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2HealthAndFood : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "RestModeFrom",
                table: "Characters",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DailySettlements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    TargetKcal = table.Column<int>(type: "integer", nullable: false),
                    ActualKcal = table.Column<int>(type: "integer", nullable: false),
                    BurnedKcal = table.Column<int>(type: "integer", nullable: false),
                    ProteinG = table.Column<int>(type: "integer", nullable: false),
                    ProteinTargetG = table.Column<int>(type: "integer", nullable: false),
                    MealsLogged = table.Column<int>(type: "integer", nullable: false),
                    QuestCompletion = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    Damage = table.Column<int>(type: "integer", nullable: false),
                    Heal = table.Column<int>(type: "integer", nullable: false),
                    IsPerfect = table.Column<bool>(type: "boolean", nullable: false),
                    PerfectStreak = table.Column<int>(type: "integer", nullable: false),
                    IsRestMode = table.Column<bool>(type: "boolean", nullable: false),
                    HpBefore = table.Column<int>(type: "integer", nullable: false),
                    HpAfter = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailySettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailySettlements_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Foods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "text", nullable: false),
                    NameVi = table.Column<string>(type: "text", nullable: false),
                    Serving = table.Column<string>(type: "text", nullable: false),
                    ServingG = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    Kcal = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    ProteinG = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    CarbG = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    FatG = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    PriceVnd = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Foods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HpEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HpEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HpEvents_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MealLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Slot = table.Column<string>(type: "text", nullable: false),
                    FoodId = table.Column<int>(type: "integer", nullable: false),
                    Servings = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealLogs_Foods_FoodId",
                        column: x => x.FoodId,
                        principalTable: "Foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailySettlements_CharacterId_Date",
                table: "DailySettlements",
                columns: new[] { "CharacterId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Foods_Code",
                table: "Foods",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HpEvents_CharacterId_Date",
                table: "HpEvents",
                columns: new[] { "CharacterId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MealLogs_FoodId",
                table: "MealLogs",
                column: "FoodId");

            migrationBuilder.CreateIndex(
                name: "IX_MealLogs_UserId_Date",
                table: "MealLogs",
                columns: new[] { "UserId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailySettlements");

            migrationBuilder.DropTable(
                name: "HpEvents");

            migrationBuilder.DropTable(
                name: "MealLogs");

            migrationBuilder.DropTable(
                name: "Foods");

            migrationBuilder.DropColumn(
                name: "RestModeFrom",
                table: "Characters");
        }
    }
}
