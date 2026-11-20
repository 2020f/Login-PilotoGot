using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Login.Migrations
{
    /// <inheritdoc />
    public partial class EnvirapidoRetornosYCajaEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PuntoRuta_Tipo",
                table: "PuntosRutaOperativos");

            migrationBuilder.DropIndex(
                name: "IX_OrdenesEntrega_ClienteAppId_CodigoA",
                table: "OrdenesEntrega");

            migrationBuilder.AddColumn<DateTime>(
                name: "DevueltaAt",
                table: "ParadasEntrega",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalizadaAt",
                table: "OrdenesEntrega",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FechaPago",
                table: "LiquidacionesOperativas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EntregadoEmpresaAt",
                table: "CobrosOperativos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ImporteEntregadoEmpresa",
                table: "CobrosOperativos",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecibidoEmpresaPor",
                table: "CobrosOperativos",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenciaEntregaEmpresa",
                table: "CobrosOperativos",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DevolucionesOperativas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrdenEntregaId = table.Column<int>(type: "int", nullable: false),
                    ParadaEntregaId = table.Column<int>(type: "int", nullable: false),
                    PilotoId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Abierta = table.Column<bool>(type: "bit", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SolicitadaPor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DecididaPor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    NotaDecision = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AutorizadaAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IniciadaAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RecibidaAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TiendaRecepcionId = table.Column<int>(type: "int", nullable: true),
                    RecibidaPor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ObservacionesRecepcion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevolucionesOperativas", x => x.Id);
                    table.UniqueConstraint("AK_DevolucionesOperativas_ClienteAppId_Id", x => new { x.ClienteAppId, x.Id });
                    table.ForeignKey(
                        name: "FK_DevolucionesOperativas_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DevolucionesOperativas_OrdenesEntrega_ClienteAppId_OrdenEntregaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId },
                        principalTable: "OrdenesEntrega",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DevolucionesOperativas_ParadasEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId, x.ParadaEntregaId },
                        principalTable: "ParadasEntrega",
                        principalColumns: new[] { "ClienteAppId", "OrdenEntregaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DevolucionesOperativas_Pilotos_ClienteAppId_PilotoId",
                        columns: x => new { x.ClienteAppId, x.PilotoId },
                        principalTable: "Pilotos",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DevolucionesOperativas_Tiendas_ClienteAppId_TiendaRecepcionId",
                        columns: x => new { x.ClienteAppId, x.TiendaRecepcionId },
                        principalTable: "Tiendas",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PuntoRuta_Tipo",
                table: "PuntosRutaOperativos",
                sql: "([Tipo] = 1 AND [ParadaEntregaId] IS NULL) OR ([Tipo] IN (2, 3) AND [ParadaEntregaId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesEntrega_ClienteAppId_CodigoA",
                table: "OrdenesEntrega",
                columns: new[] { "ClienteAppId", "CodigoA" },
                unique: true,
                filter: "[CodigoA] IS NOT NULL AND [Estado] <> 4 AND [Estado] <> 6 AND [Estado] <> 7 AND [Estado] <> 8");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CodigoEntrega_ParadaTipo",
                table: "CodigosEntrega",
                sql: "([Tipo] = 1 AND [ParadaEntregaId] IS NULL) OR [Tipo] = 2");

            migrationBuilder.CreateIndex(
                name: "IX_DevolucionesOperativas_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "DevolucionesOperativas",
                columns: new[] { "ClienteAppId", "OrdenEntregaId", "ParadaEntregaId" });

            migrationBuilder.CreateIndex(
                name: "IX_DevolucionesOperativas_ClienteAppId_ParadaEntregaId",
                table: "DevolucionesOperativas",
                columns: new[] { "ClienteAppId", "ParadaEntregaId" },
                unique: true,
                filter: "[Abierta] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_DevolucionesOperativas_ClienteAppId_PilotoId",
                table: "DevolucionesOperativas",
                columns: new[] { "ClienteAppId", "PilotoId" });

            migrationBuilder.CreateIndex(
                name: "IX_DevolucionesOperativas_ClienteAppId_TiendaRecepcionId",
                table: "DevolucionesOperativas",
                columns: new[] { "ClienteAppId", "TiendaRecepcionId" });

            migrationBuilder.Sql(@"
                UPDATE [OrdenesEntrega] SET [FinalizadaAt]=[EntregadaAt]
                WHERE [Estado]=4 AND [FinalizadaAt] IS NULL AND [EntregadaAt] IS NOT NULL;
                INSERT INTO [IncidenciasOperativas] ([ClienteAppId],[OrdenEntregaId],[Categoria],[Descripcion],
                    [Prioridad],[Estado],[Efecto],[ReportadaPor],[ClaveDeteccion],[Version],[CreatedAt],[UpdatedAt])
                SELECT o.[ClienteAppId],o.[Id],N'Legado',
                    N'Estado legado Incidente (5). Bloqueo preservado mediante caso operativo, sin SLA. '
                    + CASE WHEN o.[EntregadaAt] IS NOT NULL
                        OR (o.[RecolectadaAt] IS NOT NULL AND o.[PilotoId] IS NULL)
                        OR (o.[AssignedAt] IS NOT NULL AND o.[PilotoId] IS NULL)
                        OR EXISTS(SELECT 1 FROM [CodigosEntrega] c WHERE c.[OrdenEntregaId]=o.[Id] AND c.[Tipo]=2 AND c.[Usado]=1)
                        OR (o.[RecolectadaAt] IS NULL AND EXISTS(SELECT 1 FROM [CodigosEntrega] c
                            WHERE c.[OrdenEntregaId]=o.[Id] AND c.[Tipo]=1 AND c.[Usado]=1))
                        OR (o.[RecolectadaAt] IS NOT NULL AND NOT EXISTS(SELECT 1 FROM [CodigosEntrega] c
                            WHERE c.[OrdenEntregaId]=o.[Id] AND c.[Tipo]=1 AND c.[Usado]=1))
                      THEN N'REVISIÓN MANUAL: hitos/códigos contradictorios; no se declara entrega ni se regeneran códigos.'
                      ELSE N'Etapa logística restaurada exclusivamente según recogida registrada y piloto asignado.' END,
                    2,1,3,N'System:migracion',N'MigracionIncidente:'+CONVERT(nvarchar(20),o.[Id]),
                    REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),SYSUTCDATETIME(),SYSUTCDATETIME()
                FROM [OrdenesEntrega] o WHERE o.[Estado]=5 AND NOT EXISTS(SELECT 1 FROM [IncidenciasOperativas] i
                    WHERE i.[ClienteAppId]=o.[ClienteAppId] AND i.[ClaveDeteccion]=N'MigracionIncidente:'+CONVERT(nvarchar(20),o.[Id]));
                INSERT INTO [HistorialOrdenes] ([OrdenEntregaId],[TipoEvento],[ActorIdentityUserId],[ActorRol],[Descripcion],[CreatedAt])
                SELECT o.[Id],6,N'System:migracion',N'System',N'Migración Incidente (5): caso Legado creado, bloqueo conservado. '
                    + CASE WHEN o.[RecolectadaAt] IS NOT NULL THEN N'Etapa Recolectada por hito registrado.'
                        WHEN o.[PilotoId] IS NOT NULL THEN N'Etapa Asignada por piloto registrado.' ELSE N'Etapa Creada sin recogida/piloto verificables.' END,
                    SYSUTCDATETIME()
                FROM [OrdenesEntrega] o WHERE o.[Estado]=5 AND NOT EXISTS(SELECT 1 FROM [HistorialOrdenes] h
                    WHERE h.[OrdenEntregaId]=o.[Id] AND h.[ActorIdentityUserId]=N'System:migracion'
                    AND h.[Descripcion] LIKE N'Migración Incidente (5):%');
                UPDATE [OrdenesEntrega] SET [Estado]=CASE WHEN [RecolectadaAt] IS NOT NULL THEN 3
                    WHEN [PilotoId] IS NOT NULL THEN 2 ELSE 1 END WHERE [Estado]=5;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS(SELECT 1 FROM [DevolucionesOperativas]) OR EXISTS(SELECT 1 FROM [OrdenesEntrega] WHERE [Estado] IN (6,7,8))
                    OR EXISTS(SELECT 1 FROM [PuntosRutaOperativos] WHERE [Tipo]=3)
                    OR EXISTS(SELECT 1 FROM [CobrosOperativos] WHERE [EntregadoEmpresaAt] IS NOT NULL OR [ImporteEntregadoEmpresa] IS NOT NULL)
                    OR EXISTS(SELECT 1 FROM [LiquidacionesOperativas] WHERE [FechaPago] IS NOT NULL)
                    OR EXISTS(SELECT 1 FROM [IncidenciasOperativas] WHERE [ReportadaPor]=N'System:migracion' AND [ClaveDeteccion] LIKE N'MigracionIncidente:%')
                    THROW 51012, 'Rollback bloqueado: hay retornos, cancelaciones, caja o estados de legado reconciliados.', 1;");
            migrationBuilder.DropTable(
                name: "DevolucionesOperativas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PuntoRuta_Tipo",
                table: "PuntosRutaOperativos");

            migrationBuilder.DropIndex(
                name: "IX_OrdenesEntrega_ClienteAppId_CodigoA",
                table: "OrdenesEntrega");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CodigoEntrega_ParadaTipo",
                table: "CodigosEntrega");

            migrationBuilder.DropColumn(
                name: "DevueltaAt",
                table: "ParadasEntrega");

            migrationBuilder.DropColumn(
                name: "FinalizadaAt",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "FechaPago",
                table: "LiquidacionesOperativas");

            migrationBuilder.DropColumn(
                name: "EntregadoEmpresaAt",
                table: "CobrosOperativos");

            migrationBuilder.DropColumn(
                name: "ImporteEntregadoEmpresa",
                table: "CobrosOperativos");

            migrationBuilder.DropColumn(
                name: "RecibidoEmpresaPor",
                table: "CobrosOperativos");

            migrationBuilder.DropColumn(
                name: "ReferenciaEntregaEmpresa",
                table: "CobrosOperativos");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PuntoRuta_Tipo",
                table: "PuntosRutaOperativos",
                sql: "([Tipo] = 1 AND [ParadaEntregaId] IS NULL) OR ([Tipo] = 2 AND [ParadaEntregaId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesEntrega_ClienteAppId_CodigoA",
                table: "OrdenesEntrega",
                columns: new[] { "ClienteAppId", "CodigoA" },
                unique: true,
                filter: "[CodigoA] IS NOT NULL AND [Estado] <> 4");
        }
    }
}
