using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EWasteManagement.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCsvSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Submissions",
                type: "text",
                nullable: false,
                defaultValue: "Manual"); // every existing submission came from the item form

            migrationBuilder.AddColumn<string>(
                name: "CategoryHint",
                table: "SubmissionItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedWeightKg",
                table: "SubmissionItems",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "SubmissionItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "SubmissionItems",
                type: "integer",
                nullable: false,
                defaultValue: 1); // existing items are single units
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "CategoryHint",
                table: "SubmissionItems");

            migrationBuilder.DropColumn(
                name: "EstimatedWeightKg",
                table: "SubmissionItems");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "SubmissionItems");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "SubmissionItems");
        }
    }
}
