using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantMemberCreatedAtTicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CreatedAtTicks",
                table: "TenantMembers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAtTicks",
                table: "TenantMembers");
        }
    }
}
