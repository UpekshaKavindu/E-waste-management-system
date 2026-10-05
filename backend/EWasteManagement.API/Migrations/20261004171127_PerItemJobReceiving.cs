using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EWasteManagement.API.Migrations
{
    /// <inheritdoc />
    public partial class PerItemJobReceiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inventory_items_job_id",
                table: "inventory_items");

            migrationBuilder.AddColumn<int>(
                name: "quantity",
                table: "inventory_items",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "submission_item_id",
                table: "inventory_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_job_id_submission_item_id",
                table: "inventory_items",
                columns: new[] { "job_id", "submission_item_id" },
                unique: true,
                filter: "job_id IS NOT NULL AND submission_item_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_job_id_whole_job",
                table: "inventory_items",
                column: "job_id",
                unique: true,
                filter: "job_id IS NOT NULL AND submission_item_id IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_items_quantity",
                table: "inventory_items",
                sql: "quantity >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inventory_items_job_id_submission_item_id",
                table: "inventory_items");

            migrationBuilder.DropIndex(
                name: "IX_inventory_items_job_id_whole_job",
                table: "inventory_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_items_quantity",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "quantity",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "submission_item_id",
                table: "inventory_items");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_job_id",
                table: "inventory_items",
                column: "job_id",
                unique: true,
                filter: "job_id IS NOT NULL");
        }
    }
}
