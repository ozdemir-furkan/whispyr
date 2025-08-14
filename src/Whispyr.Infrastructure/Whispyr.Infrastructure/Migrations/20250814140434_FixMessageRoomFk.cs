using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Whispyr.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMessageRoomFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Rooms_RoomId1",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_RoomId1",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "RoomId1",
                table: "Messages");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorHash",
                table: "Messages",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "AuthorHash",
                table: "Messages",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoomId1",
                table: "Messages",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_RoomId1",
                table: "Messages",
                column: "RoomId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Rooms_RoomId1",
                table: "Messages",
                column: "RoomId1",
                principalTable: "Rooms",
                principalColumn: "Id");
        }
    }
}
