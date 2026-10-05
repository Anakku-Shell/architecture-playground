using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shop.Layered.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderLineNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LineNumber",
                table: "order_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LineNumber",
                table: "order_lines");
        }
    }
}
