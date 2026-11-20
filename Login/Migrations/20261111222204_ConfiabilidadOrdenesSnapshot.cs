using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Login.Migrations
{
    /// <inheritdoc />
    public partial class ConfiabilidadOrdenesSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DatosEntregaCongelados",
                table: "OrdenesEntrega",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DestinoDireccion",
                table: "OrdenesEntrega",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "DestinoLatitud",
                table: "OrdenesEntrega",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DestinoLongitud",
                table: "OrdenesEntrega",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinoMapaLink",
                table: "OrdenesEntrega",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinoNombre",
                table: "OrdenesEntrega",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DestinoTelefono",
                table: "OrdenesEntrega",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RecogidaDireccion",
                table: "OrdenesEntrega",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "RecogidaLatitud",
                table: "OrdenesEntrega",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RecogidaLongitud",
                table: "OrdenesEntrega",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecogidaNombre",
                table: "OrdenesEntrega",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RecogidaTelefono",
                table: "OrdenesEntrega",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            // Congelar los datos disponibles del legado sin tocar IDs, estados,
            // precios, códigos ni fechas. No es posible reconstruir direcciones
            // anteriores que ya se hubieran sobrescrito antes de esta versión.
            migrationBuilder.Sql(@"
                UPDATE o SET
                    [RecogidaNombre] = t.[Nombre],
                    [RecogidaDireccion] = t.[Direccion],
                    [RecogidaTelefono] = t.[Telefono],
                    [RecogidaLatitud] = t.[Latitud],
                    [RecogidaLongitud] = t.[Longitud],
                    [DestinoNombre] = u.[Nombre],
                    [DestinoDireccion] = u.[DireccionUbicacion],
                    [DestinoTelefono] = u.[Telefono],
                    [DestinoMapaLink] = u.[MapaLink],
                    [DestinoLatitud] = u.[Latitud],
                    [DestinoLongitud] = u.[Longitud],
                    [DatosEntregaCongelados] = 1
                FROM [OrdenesEntrega] o
                INNER JOIN [Tiendas] t ON t.[Id] = o.[TiendaId]
                INNER JOIN [UsuariosFinales] u ON u.[Id] = o.[UsuarioFinalId]
                WHERE o.[DatosEntregaCongelados] = 0;");

            migrationBuilder.CreateTable(
                name: "OperacionesOrden",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    ActorIdentityUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Clave = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OrdenEntregaId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacionesOrden", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperacionesOrden_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperacionesOrden_OrdenesEntrega_OrdenEntregaId",
                        column: x => x.OrdenEntregaId,
                        principalTable: "OrdenesEntrega",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesOrden_ClienteAppId_ActorIdentityUserId_Tipo_Clave",
                table: "OperacionesOrden",
                columns: new[] { "ClienteAppId", "ActorIdentityUserId", "Tipo", "Clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesOrden_OrdenEntregaId",
                table: "OperacionesOrden",
                column: "OrdenEntregaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM [OperacionesOrden])
                   OR EXISTS (SELECT 1 FROM [OrdenesEntrega] WHERE [DatosEntregaCongelados] = 1)
                    THROW 51001, 'No eliminar snapshots ni operaciones confirmadas. Restaurar el respaldo previo para revertir.', 1;");

            migrationBuilder.DropTable(
                name: "OperacionesOrden");

            migrationBuilder.DropColumn(
                name: "DatosEntregaCongelados",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "DestinoDireccion",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "DestinoLatitud",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "DestinoLongitud",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "DestinoMapaLink",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "DestinoNombre",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "DestinoTelefono",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "RecogidaDireccion",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "RecogidaLatitud",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "RecogidaLongitud",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "RecogidaNombre",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "RecogidaTelefono",
                table: "OrdenesEntrega");
        }
    }
}
