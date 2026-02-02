using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Login.Migrations
{
    /// <inheritdoc />
    public partial class campoExtrasClienteFinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Activo",
                table: "ClientesApp",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactoResponsable",
                table: "ClientesApp",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "ClientesApp",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EsTrial",
                table: "ClientesApp",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaFinPlan",
                table: "ClientesApp",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaInicioPlan",
                table: "ClientesApp",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxOrdenesMes",
                table: "ClientesApp",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxPilotos",
                table: "ClientesApp",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxUsuarios",
                table: "ClientesApp",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Observaciones",
                table: "ClientesApp",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProximoCobro",
                table: "ClientesApp",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RNC",
                table: "ClientesApp",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimoPago",
                table: "ClientesApp",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ClientesApp",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "ClientesApp",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Activo",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "ContactoResponsable",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "EsTrial",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "FechaFinPlan",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "FechaInicioPlan",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "MaxOrdenesMes",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "MaxPilotos",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "MaxUsuarios",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "Observaciones",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "ProximoCobro",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "RNC",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "UltimoPago",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ClientesApp");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "ClientesApp");
        }
    }
}
