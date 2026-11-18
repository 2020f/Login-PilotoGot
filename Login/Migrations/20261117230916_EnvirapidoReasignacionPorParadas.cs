using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Login.Migrations
{
    /// <inheritdoc />
    public partial class EnvirapidoReasignacionPorParadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La relación anterior no utilizaba RutaOrdenId como FK. Conservar
            // las visitas históricas enlazándolas a su membresía real antes de
            // admitir varias paradas de una orden en la misma ruta.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM PuntosRutaOperativos p WHERE NOT EXISTS
    (SELECT 1 FROM RutaOrdenesOperativas r WHERE r.ClienteAppId=p.ClienteAppId AND r.RutaId=p.RutaId AND r.OrdenEntregaId=p.OrdenEntregaId))
    THROW 51001, 'Existen visitas sin membresia de ruta; revisar antes de migrar.', 1;
UPDATE p SET RutaOrdenId=r.Id FROM PuntosRutaOperativos p
INNER JOIN RutaOrdenesOperativas r ON r.ClienteAppId=p.ClienteAppId AND r.RutaId=p.RutaId AND r.OrdenEntregaId=p.OrdenEntregaId;");
            migrationBuilder.DropForeignKey(
                name: "FK_PuntosRutaOperativos_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId",
                table: "PuntosRutaOperativos");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId",
                table: "RutaOrdenesOperativas");

            migrationBuilder.DropIndex(
                name: "IX_RutaOrdenesOperativas_ClienteAppId_OrdenEntregaId",
                table: "RutaOrdenesOperativas");

            migrationBuilder.DropIndex(
                name: "IX_PuntosRutaOperativos_ClienteAppId_RutaId_OrdenEntregaId",
                table: "PuntosRutaOperativos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PuntoRuta_Tipo",
                table: "PuntosRutaOperativos");

            migrationBuilder.AddColumn<int>(
                name: "ParadaEntregaId",
                table: "RutaOrdenesOperativas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecogidaConfirmadaAt",
                table: "PuntosRutaOperativos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnReasignacion",
                table: "ParadasEntrega",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PilotoAsignadoId",
                table: "ParadasEntrega",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PilotoCustodioId",
                table: "ParadasEntrega",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PilotoEntregaId",
                table: "ParadasEntrega",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecogidaActualDireccion",
                table: "ParadasEntrega",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RecogidaActualLatitud",
                table: "ParadasEntrega",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RecogidaActualLongitud",
                table: "ParadasEntrega",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecogidaActualNombre",
                table: "ParadasEntrega",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecogidaParadaAt",
                table: "ParadasEntrega",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevisionAsignacion",
                table: "ParadasEntrega",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "GestionPorParadas",
                table: "OrdenesEntrega",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId_Id",
                table: "RutaOrdenesOperativas",
                columns: new[] { "ClienteAppId", "RutaId", "OrdenEntregaId", "Id" });

            migrationBuilder.CreateTable(
                name: "RetirosParadas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrdenEntregaId = table.Column<int>(type: "int", nullable: false),
                    ParadaEntregaId = table.Column<int>(type: "int", nullable: false),
                    PilotoOrigenId = table.Column<int>(type: "int", nullable: true),
                    PilotoDestinoId = table.Column<int>(type: "int", nullable: true),
                    TiendaDestinoId = table.Column<int>(type: "int", nullable: true),
                    IncidenciaId = table.Column<int>(type: "int", nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TipoDestino = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    DestinoNombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    DestinoDireccion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    DestinoLatitud = table.Column<double>(type: "float", nullable: true),
                    DestinoLongitud = table.Column<double>(type: "float", nullable: true),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    NotaDecision = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SolicitadaPor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DecididaPor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RecibidaPor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RecibidaAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Abierta = table.Column<bool>(type: "bit", nullable: false),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetirosParadas", x => x.Id);
                    table.UniqueConstraint("AK_RetirosParadas_ClienteAppId_Id", x => new { x.ClienteAppId, x.Id });
                    table.CheckConstraint("CK_Retiro_Estado", "[Estado] IN ('Solicitada','Rechazada','PorReasignar','PendienteRecepcion','Recibida','Reasignada','Cancelada')");
                    table.ForeignKey(
                        name: "FK_RetirosParadas_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RetirosParadas_IncidenciasOperativas_ClienteAppId_IncidenciaId",
                        columns: x => new { x.ClienteAppId, x.IncidenciaId },
                        principalTable: "IncidenciasOperativas",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RetirosParadas_OrdenesEntrega_ClienteAppId_OrdenEntregaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId },
                        principalTable: "OrdenesEntrega",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RetirosParadas_ParadasEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId, x.ParadaEntregaId },
                        principalTable: "ParadasEntrega",
                        principalColumns: new[] { "ClienteAppId", "OrdenEntregaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RetirosParadas_Pilotos_ClienteAppId_PilotoDestinoId",
                        columns: x => new { x.ClienteAppId, x.PilotoDestinoId },
                        principalTable: "Pilotos",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RetirosParadas_Pilotos_ClienteAppId_PilotoOrigenId",
                        columns: x => new { x.ClienteAppId, x.PilotoOrigenId },
                        principalTable: "Pilotos",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RetirosParadas_Tiendas_ClienteAppId_TiendaDestinoId",
                        columns: x => new { x.ClienteAppId, x.TiendaDestinoId },
                        principalTable: "Tiendas",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RutaOrdenesOperativas_ClienteAppId_OrdenEntregaId",
                table: "RutaOrdenesOperativas",
                columns: new[] { "ClienteAppId", "OrdenEntregaId" },
                unique: true,
                filter: "[Vigente] = 1 AND [ParadaEntregaId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RutaOrdenesOperativas_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "RutaOrdenesOperativas",
                columns: new[] { "ClienteAppId", "OrdenEntregaId", "ParadaEntregaId" });

            migrationBuilder.CreateIndex(
                name: "IX_RutaOrdenesOperativas_ClienteAppId_ParadaEntregaId",
                table: "RutaOrdenesOperativas",
                columns: new[] { "ClienteAppId", "ParadaEntregaId" },
                unique: true,
                filter: "[Vigente] = 1 AND [ParadaEntregaId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PuntosRutaOperativos_ClienteAppId_RutaId_OrdenEntregaId_RutaOrdenId",
                table: "PuntosRutaOperativos",
                columns: new[] { "ClienteAppId", "RutaId", "OrdenEntregaId", "RutaOrdenId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PuntoRuta_Tipo",
                table: "PuntosRutaOperativos",
                sql: "[Tipo] = 1 OR ([Tipo] IN (2, 3) AND [ParadaEntregaId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ParadasEntrega_ClienteAppId_PilotoAsignadoId",
                table: "ParadasEntrega",
                columns: new[] { "ClienteAppId", "PilotoAsignadoId" });

            migrationBuilder.CreateIndex(
                name: "IX_ParadasEntrega_ClienteAppId_PilotoCustodioId",
                table: "ParadasEntrega",
                columns: new[] { "ClienteAppId", "PilotoCustodioId" });

            migrationBuilder.CreateIndex(
                name: "IX_ParadasEntrega_ClienteAppId_PilotoEntregaId",
                table: "ParadasEntrega",
                columns: new[] { "ClienteAppId", "PilotoEntregaId" });

            migrationBuilder.CreateIndex(
                name: "IX_RetirosParadas_ClienteAppId_IncidenciaId",
                table: "RetirosParadas",
                columns: new[] { "ClienteAppId", "IncidenciaId" });

            migrationBuilder.CreateIndex(
                name: "IX_RetirosParadas_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "RetirosParadas",
                columns: new[] { "ClienteAppId", "OrdenEntregaId", "ParadaEntregaId" });

            migrationBuilder.CreateIndex(
                name: "IX_RetirosParadas_ClienteAppId_ParadaEntregaId",
                table: "RetirosParadas",
                columns: new[] { "ClienteAppId", "ParadaEntregaId" },
                unique: true,
                filter: "[Abierta] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_RetirosParadas_ClienteAppId_PilotoDestinoId",
                table: "RetirosParadas",
                columns: new[] { "ClienteAppId", "PilotoDestinoId" });

            migrationBuilder.CreateIndex(
                name: "IX_RetirosParadas_ClienteAppId_PilotoOrigenId",
                table: "RetirosParadas",
                columns: new[] { "ClienteAppId", "PilotoOrigenId" });

            migrationBuilder.CreateIndex(
                name: "IX_RetirosParadas_ClienteAppId_TiendaDestinoId",
                table: "RetirosParadas",
                columns: new[] { "ClienteAppId", "TiendaDestinoId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ParadasEntrega_Pilotos_ClienteAppId_PilotoAsignadoId",
                table: "ParadasEntrega",
                columns: new[] { "ClienteAppId", "PilotoAsignadoId" },
                principalTable: "Pilotos",
                principalColumns: new[] { "ClienteAppId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ParadasEntrega_Pilotos_ClienteAppId_PilotoCustodioId",
                table: "ParadasEntrega",
                columns: new[] { "ClienteAppId", "PilotoCustodioId" },
                principalTable: "Pilotos",
                principalColumns: new[] { "ClienteAppId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ParadasEntrega_Pilotos_ClienteAppId_PilotoEntregaId",
                table: "ParadasEntrega",
                columns: new[] { "ClienteAppId", "PilotoEntregaId" },
                principalTable: "Pilotos",
                principalColumns: new[] { "ClienteAppId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PuntosRutaOperativos_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId_RutaOrdenId",
                table: "PuntosRutaOperativos",
                columns: new[] { "ClienteAppId", "RutaId", "OrdenEntregaId", "RutaOrdenId" },
                principalTable: "RutaOrdenesOperativas",
                principalColumns: new[] { "ClienteAppId", "RutaId", "OrdenEntregaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RutaOrdenesOperativas_ParadasEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "RutaOrdenesOperativas",
                columns: new[] { "ClienteAppId", "OrdenEntregaId", "ParadaEntregaId" },
                principalTable: "ParadasEntrega",
                principalColumns: new[] { "ClienteAppId", "OrdenEntregaId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM OrdenesEntrega WHERE GestionPorParadas=1) OR EXISTS (SELECT 1 FROM RetirosParadas)
    THROW 51002, 'No se puede revertir el esquema con reasignaciones operativas. Conservar los datos y recuperar el release compatible.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_ParadasEntrega_Pilotos_ClienteAppId_PilotoAsignadoId",
                table: "ParadasEntrega");

            migrationBuilder.DropForeignKey(
                name: "FK_ParadasEntrega_Pilotos_ClienteAppId_PilotoCustodioId",
                table: "ParadasEntrega");

            migrationBuilder.DropForeignKey(
                name: "FK_ParadasEntrega_Pilotos_ClienteAppId_PilotoEntregaId",
                table: "ParadasEntrega");

            migrationBuilder.DropForeignKey(
                name: "FK_PuntosRutaOperativos_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId_RutaOrdenId",
                table: "PuntosRutaOperativos");

            migrationBuilder.DropForeignKey(
                name: "FK_RutaOrdenesOperativas_ParadasEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "RutaOrdenesOperativas");

            migrationBuilder.DropTable(
                name: "RetirosParadas");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId_Id",
                table: "RutaOrdenesOperativas");

            migrationBuilder.DropIndex(
                name: "IX_RutaOrdenesOperativas_ClienteAppId_OrdenEntregaId",
                table: "RutaOrdenesOperativas");

            migrationBuilder.DropIndex(
                name: "IX_RutaOrdenesOperativas_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "RutaOrdenesOperativas");

            migrationBuilder.DropIndex(
                name: "IX_RutaOrdenesOperativas_ClienteAppId_ParadaEntregaId",
                table: "RutaOrdenesOperativas");

            migrationBuilder.DropIndex(
                name: "IX_PuntosRutaOperativos_ClienteAppId_RutaId_OrdenEntregaId_RutaOrdenId",
                table: "PuntosRutaOperativos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PuntoRuta_Tipo",
                table: "PuntosRutaOperativos");

            migrationBuilder.DropIndex(
                name: "IX_ParadasEntrega_ClienteAppId_PilotoAsignadoId",
                table: "ParadasEntrega");

            migrationBuilder.DropIndex(
                name: "IX_ParadasEntrega_ClienteAppId_PilotoCustodioId",
                table: "ParadasEntrega");

            migrationBuilder.DropIndex(
                name: "IX_ParadasEntrega_ClienteAppId_PilotoEntregaId",
                table: "ParadasEntrega");

            migrationBuilder.DropColumn(
                name: "ParadaEntregaId",
                table: "RutaOrdenesOperativas");

            migrationBuilder.DropColumn(
                name: "RecogidaConfirmadaAt",
                table: "PuntosRutaOperativos");

            migrationBuilder.DropColumn(
                name: "EnReasignacion",
                table: "ParadasEntrega");

            migrationBuilder.DropColumn(
                name: "PilotoAsignadoId",
                table: "ParadasEntrega");

            migrationBuilder.DropColumn(
                name: "PilotoCustodioId",
                table: "ParadasEntrega");

            migrationBuilder.DropColumn(
                name: "PilotoEntregaId",
                table: "ParadasEntrega");

            migrationBuilder.DropColumn(
                name: "RecogidaActualDireccion",
                table: "ParadasEntrega");

            migrationBuilder.DropColumn(
                name: "RecogidaActualLatitud",
                table: "ParadasEntrega");

            migrationBuilder.DropColumn(
                name: "RecogidaActualLongitud",
                table: "ParadasEntrega");

            migrationBuilder.DropColumn(
                name: "RecogidaActualNombre",
                table: "ParadasEntrega");

            migrationBuilder.DropColumn(
                name: "RecogidaParadaAt",
                table: "ParadasEntrega");

            migrationBuilder.DropColumn(
                name: "RevisionAsignacion",
                table: "ParadasEntrega");

            migrationBuilder.DropColumn(
                name: "GestionPorParadas",
                table: "OrdenesEntrega");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId",
                table: "RutaOrdenesOperativas",
                columns: new[] { "ClienteAppId", "RutaId", "OrdenEntregaId" });

            migrationBuilder.CreateIndex(
                name: "IX_RutaOrdenesOperativas_ClienteAppId_OrdenEntregaId",
                table: "RutaOrdenesOperativas",
                columns: new[] { "ClienteAppId", "OrdenEntregaId" },
                unique: true,
                filter: "[Vigente] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PuntosRutaOperativos_ClienteAppId_RutaId_OrdenEntregaId",
                table: "PuntosRutaOperativos",
                columns: new[] { "ClienteAppId", "RutaId", "OrdenEntregaId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PuntoRuta_Tipo",
                table: "PuntosRutaOperativos",
                sql: "([Tipo] = 1 AND [ParadaEntregaId] IS NULL) OR ([Tipo] IN (2, 3) AND [ParadaEntregaId] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_PuntosRutaOperativos_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId",
                table: "PuntosRutaOperativos",
                columns: new[] { "ClienteAppId", "RutaId", "OrdenEntregaId" },
                principalTable: "RutaOrdenesOperativas",
                principalColumns: new[] { "ClienteAppId", "RutaId", "OrdenEntregaId" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
