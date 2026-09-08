using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Login.Migrations
{
    /// <inheritdoc />
    public partial class AddGpsPrecios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Latitud",
                table: "UsuariosFinales",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitud",
                table: "UsuariosFinales",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitud",
                table: "Tiendas",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitud",
                table: "Tiendas",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitud",
                table: "Pilotos",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitud",
                table: "Pilotos",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimaUbicacionAt",
                table: "Pilotos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DistanciaKm",
                table: "OrdenesEntrega",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Precio",
                table: "OrdenesEntrega",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PrecioBase",
                table: "ClientesApp",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PrecioPorKm",
                table: "ClientesApp",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Latitud",
                table: "UsuariosFinales");

            migrationBuilder.DropColumn(
                name: "Longitud",
                table: "UsuariosFinales");

            migrationBuilder.DropColumn(
                name: "Latitud",
                table: "Tiendas");

            migrationBuilder.DropColumn(
                name: "Longitud",
                table: "Tiendas");

            migrationBuilder.DropColumn(
                name: "Latitud",
                table: "Pilotos");

            migrationBuilder.DropColumn(
                name: "Longitud",
                table: "Pilotos");

            migrationBuilder.DropColumn(
                name: "UltimaUbicacionAt",
                table: "Pilotos");

            migrationBuilder.DropColumn(
                name: "DistanciaKm",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "Precio",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "PrecioBase",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "PrecioPorKm",
                table: "ClientesApp");
        }
    }
}
