using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DragonFit.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class QuestionPattern : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Pattern",
                table: "OnboardingQuestions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PatternMessage",
                table: "OnboardingQuestions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Pattern",
                table: "OnboardingQuestions");

            migrationBuilder.DropColumn(
                name: "PatternMessage",
                table: "OnboardingQuestions");
        }
    }
}
