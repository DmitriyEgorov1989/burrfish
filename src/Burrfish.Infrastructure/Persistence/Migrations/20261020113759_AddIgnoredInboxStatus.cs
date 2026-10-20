using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Burrfish.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIgnoredInboxStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_inbox_status",
                table: "inbox_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inbox_status",
                table: "inbox_events",
                sql: "status IN ('Pending', 'Processing', 'Processed', 'Failed', 'Ignored')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_inbox_status",
                table: "inbox_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inbox_status",
                table: "inbox_events",
                sql: "status IN ('Pending', 'Processing', 'Processed', 'Failed')");
        }
    }
}
