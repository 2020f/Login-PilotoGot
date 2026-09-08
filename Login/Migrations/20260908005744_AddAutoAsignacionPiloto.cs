using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Login.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoAsignacionPiloto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoAsignacion",
                table: "Tiendas",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EsAuto",
                table: "OrdenesEntrega",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "OrdenesAutoRechazo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrdenEntregaId = table.Column<int>(type: "int", nullable: false),
                    PilotoId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdenesAutoRechazo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrdenesAutoRechazo_OrdenesEntrega_OrdenEntregaId",
                        column: x => x.OrdenEntregaId,
                        principalTable: "OrdenesEntrega",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrdenesAutoRechazo_Pilotos_PilotoId",
                        column: x => x.PilotoId,
                        principalTable: "Pilotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesAutoRechazo_OrdenEntregaId_PilotoId",
                table: "OrdenesAutoRechazo",
                columns: new[] { "OrdenEntregaId", "PilotoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesAutoRechazo_PilotoId",
                table: "OrdenesAutoRechazo",
                column: "PilotoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrdenesAutoRechazo");

            migrationBuilder.DropColumn(
                name: "AutoAsignacion",
                table: "Tiendas");

            migrationBuilder.DropColumn(
                name: "EsAuto",
                table: "OrdenesEntrega");
        }
    }
}
