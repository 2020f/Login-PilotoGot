using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Login.Migrations
{
    /// <inheritdoc />
    public partial class EnvirapidoParadasOperaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CodigosEntrega_OrdenesEntrega_OrdenEntregaId",
                table: "CodigosEntrega");

            migrationBuilder.DropIndex(
                name: "IX_UsuariosFinales_TiendaId",
                table: "UsuariosFinales");

            migrationBuilder.DropIndex(
                name: "IX_Tiendas_ClienteAppId",
                table: "Tiendas");

            migrationBuilder.DropIndex(
                name: "IX_Pilotos_ClienteAppId",
                table: "Pilotos");

            migrationBuilder.DropIndex(
                name: "IX_CodigosEntrega_OrdenEntregaId_Tipo",
                table: "CodigosEntrega");

            migrationBuilder.AlterColumn<int>(
                name: "UsuarioFinalId",
                table: "OrdenesEntrega",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "ResultadoJson",
                table: "OperacionesOrden",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParadaEntregaId",
                table: "CodigosEntrega",
                type: "int",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_UsuariosFinales_TiendaId_Id",
                table: "UsuariosFinales",
                columns: new[] { "TiendaId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Tiendas_ClienteAppId_Id",
                table: "Tiendas",
                columns: new[] { "ClienteAppId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Pilotos_ClienteAppId_Id",
                table: "Pilotos",
                columns: new[] { "ClienteAppId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrdenesEntrega_ClienteAppId_Id",
                table: "OrdenesEntrega",
                columns: new[] { "ClienteAppId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrdenesEntrega_ClienteAppId_Id_TiendaId",
                table: "OrdenesEntrega",
                columns: new[] { "ClienteAppId", "Id", "TiendaId" });

            migrationBuilder.CreateTable(
                name: "DatosEnviosOperativos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrdenEntregaId = table.Column<int>(type: "int", nullable: false),
                    Referencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Servicio = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Paquetes = table.Column<int>(type: "int", nullable: false),
                    PesoKg = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Prioridad = table.Column<int>(type: "int", nullable: false),
                    ZonaId = table.Column<int>(type: "int", nullable: true),
                    ProgramadaPara = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompromisoEntrega = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceladaAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoCancelacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NotasOperativas = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatosEnviosOperativos", x => x.Id);
                    table.UniqueConstraint("AK_DatosEnviosOperativos_ClienteAppId_Id", x => new { x.ClienteAppId, x.Id });
                    table.ForeignKey(
                        name: "FK_DatosEnviosOperativos_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DatosEnviosOperativos_OrdenesEntrega_ClienteAppId_OrdenEntregaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId },
                        principalTable: "OrdenesEntrega",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EventosOperativos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RecursoId = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosOperativos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosOperativos_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LiquidacionesOperativas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PilotoId = table.Column<int>(type: "int", nullable: true),
                    TiendaId = table.Column<int>(type: "int", nullable: true),
                    Desde = table.Column<DateOnly>(type: "date", nullable: false),
                    Hasta = table.Column<DateOnly>(type: "date", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Moneda = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Servicios = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Incentivos = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Comisiones = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Ajustes = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EfectivoCompensado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Neto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Entregas = table.Column<int>(type: "int", nullable: false),
                    Conciliada = table.Column<bool>(type: "bit", nullable: false),
                    MetodoPago = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CuentaUltimos4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    FechaProgramada = table.Column<DateOnly>(type: "date", nullable: true),
                    PagadaAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReferenciaPago = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    AprobadaPor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiquidacionesOperativas", x => x.Id);
                    table.UniqueConstraint("AK_LiquidacionesOperativas_ClienteAppId_Id", x => new { x.ClienteAppId, x.Id });
                    table.CheckConstraint("CK_Liquidacion_Beneficiario", "([PilotoId] IS NOT NULL AND [TiendaId] IS NULL) OR ([PilotoId] IS NULL AND [TiendaId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_LiquidacionesOperativas_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiquidacionesOperativas_Pilotos_ClienteAppId_PilotoId",
                        columns: x => new { x.ClienteAppId, x.PilotoId },
                        principalTable: "Pilotos",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiquidacionesOperativas_Tiendas_ClienteAppId_TiendaId",
                        columns: x => new { x.ClienteAppId, x.TiendaId },
                        principalTable: "Tiendas",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperacionesEnvirapido",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Clave = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResultadoJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacionesEnvirapido", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperacionesEnvirapido_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ParadasEntrega",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    OrdenEntregaId = table.Column<int>(type: "int", nullable: false),
                    UsuarioFinalId = table.Column<int>(type: "int", nullable: false),
                    TiendaId = table.Column<int>(type: "int", nullable: false),
                    Posicion = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Direccion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Telefono = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MapaLink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Latitud = table.Column<double>(type: "float", nullable: true),
                    Longitud = table.Column<double>(type: "float", nullable: true),
                    Instrucciones = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    EntregadaAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParadasEntrega", x => x.Id);
                    table.UniqueConstraint("AK_ParadasEntrega_ClienteAppId_OrdenEntregaId_Id", x => new { x.ClienteAppId, x.OrdenEntregaId, x.Id });
                    table.ForeignKey(
                        name: "FK_ParadasEntrega_OrdenesEntrega_ClienteAppId_OrdenEntregaId_TiendaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId, x.TiendaId },
                        principalTable: "OrdenesEntrega",
                        principalColumns: new[] { "ClienteAppId", "Id", "TiendaId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ParadasEntrega_UsuariosFinales_TiendaId_UsuarioFinalId",
                        columns: x => new { x.TiendaId, x.UsuarioFinalId },
                        principalTable: "UsuariosFinales",
                        principalColumns: new[] { "TiendaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosicionesPilotosOperativas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    PilotoId = table.Column<int>(type: "int", nullable: false),
                    DispositivoId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Secuencia = table.Column<long>(type: "bigint", nullable: false),
                    Latitud = table.Column<double>(type: "float", nullable: false),
                    Longitud = table.Column<double>(type: "float", nullable: false),
                    Precision = table.Column<double>(type: "float", nullable: true),
                    CapturadoAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecibidoAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosicionesPilotosOperativas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosicionesPilotosOperativas_Pilotos_ClienteAppId_PilotoId",
                        columns: x => new { x.ClienteAppId, x.PilotoId },
                        principalTable: "Pilotos",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReglasIncidencia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Categoria = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Prioridad = table.Column<int>(type: "int", nullable: false),
                    MinutosResolucion = table.Column<int>(type: "int", nullable: true),
                    Efecto = table.Column<int>(type: "int", nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    DetectarDemora = table.Column<bool>(type: "bit", nullable: false),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReglasIncidencia", x => x.Id);
                    table.UniqueConstraint("AK_ReglasIncidencia_ClienteAppId_Id", x => new { x.ClienteAppId, x.Id });
                    table.ForeignKey(
                        name: "FK_ReglasIncidencia_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReglasRemuneracion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Unidad = table.Column<int>(type: "int", nullable: false),
                    ImporteFijo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PorcentajeTarifa = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IncentivoFijo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ComisionPorcentaje = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReglasRemuneracion", x => x.Id);
                    table.UniqueConstraint("AK_ReglasRemuneracion_ClienteAppId_Id", x => new { x.ClienteAppId, x.Id });
                    table.ForeignKey(
                        name: "FK_ReglasRemuneracion_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ZonasOperativas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PoligonoJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuperficieKm2 = table.Column<double>(type: "float", nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    HoraInicio = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    HoraFin = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Capacidad = table.Column<int>(type: "int", nullable: false),
                    UnidadCapacidad = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AvisoPorcentaje = table.Column<int>(type: "int", nullable: false),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZonasOperativas", x => x.Id);
                    table.UniqueConstraint("AK_ZonasOperativas_ClienteAppId_Id", x => new { x.ClienteAppId, x.Id });
                    table.ForeignKey(
                        name: "FK_ZonasOperativas_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CobrosOperativos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrdenEntregaId = table.Column<int>(type: "int", nullable: false),
                    ParadaEntregaId = table.Column<int>(type: "int", nullable: true),
                    PilotoId = table.Column<int>(type: "int", nullable: false),
                    TiendaId = table.Column<int>(type: "int", nullable: false),
                    Importe = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ImporteVerificado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Referencia = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Nota = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConciliadoAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LiquidacionId = table.Column<int>(type: "int", nullable: true),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CobrosOperativos", x => x.Id);
                    table.UniqueConstraint("AK_CobrosOperativos_ClienteAppId_Id", x => new { x.ClienteAppId, x.Id });
                    table.ForeignKey(
                        name: "FK_CobrosOperativos_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CobrosOperativos_LiquidacionesOperativas_ClienteAppId_LiquidacionId",
                        columns: x => new { x.ClienteAppId, x.LiquidacionId },
                        principalTable: "LiquidacionesOperativas",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CobrosOperativos_OrdenesEntrega_ClienteAppId_OrdenEntregaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId },
                        principalTable: "OrdenesEntrega",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CobrosOperativos_ParadasEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId, x.ParadaEntregaId },
                        principalTable: "ParadasEntrega",
                        principalColumns: new[] { "ClienteAppId", "OrdenEntregaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CobrosOperativos_Pilotos_ClienteAppId_PilotoId",
                        columns: x => new { x.ClienteAppId, x.PilotoId },
                        principalTable: "Pilotos",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CobrosOperativos_Tiendas_ClienteAppId_TiendaId",
                        columns: x => new { x.ClienteAppId, x.TiendaId },
                        principalTable: "Tiendas",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RutasOperativas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    ZonaId = table.Column<int>(type: "int", nullable: true),
                    PilotoId = table.Column<int>(type: "int", nullable: true),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    PermitirReordenarPiloto = table.Column<bool>(type: "bit", nullable: false),
                    IniciadaAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinalizadaAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RutasOperativas", x => x.Id);
                    table.UniqueConstraint("AK_RutasOperativas_ClienteAppId_Id", x => new { x.ClienteAppId, x.Id });
                    table.ForeignKey(
                        name: "FK_RutasOperativas_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RutasOperativas_Pilotos_ClienteAppId_PilotoId",
                        columns: x => new { x.ClienteAppId, x.PilotoId },
                        principalTable: "Pilotos",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RutasOperativas_ZonasOperativas_ClienteAppId_ZonaId",
                        columns: x => new { x.ClienteAppId, x.ZonaId },
                        principalTable: "ZonasOperativas",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IncidenciasOperativas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrdenEntregaId = table.Column<int>(type: "int", nullable: true),
                    ParadaEntregaId = table.Column<int>(type: "int", nullable: true),
                    RutaId = table.Column<int>(type: "int", nullable: true),
                    Categoria = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Prioridad = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Efecto = table.Column<int>(type: "int", nullable: false),
                    ReportadaPor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ResponsableId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    VenceAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResueltaAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Resolucion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReglaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ClaveDeteccion = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidenciasOperativas", x => x.Id);
                    table.UniqueConstraint("AK_IncidenciasOperativas_ClienteAppId_Id", x => new { x.ClienteAppId, x.Id });
                    table.CheckConstraint("CK_Incidencia_Ambito", "([OrdenEntregaId] IS NOT NULL OR [RutaId] IS NOT NULL) AND ([ParadaEntregaId] IS NULL OR [OrdenEntregaId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_IncidenciasOperativas_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncidenciasOperativas_OrdenesEntrega_ClienteAppId_OrdenEntregaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId },
                        principalTable: "OrdenesEntrega",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncidenciasOperativas_ParadasEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId, x.ParadaEntregaId },
                        principalTable: "ParadasEntrega",
                        principalColumns: new[] { "ClienteAppId", "OrdenEntregaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncidenciasOperativas_RutasOperativas_ClienteAppId_RutaId",
                        columns: x => new { x.ClienteAppId, x.RutaId },
                        principalTable: "RutasOperativas",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovimientosOperativos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LiquidacionId = table.Column<int>(type: "int", nullable: false),
                    OrdenEntregaId = table.Column<int>(type: "int", nullable: true),
                    ParadaEntregaId = table.Column<int>(type: "int", nullable: true),
                    RutaId = table.Column<int>(type: "int", nullable: true),
                    Concepto = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClaveOrigen = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Importe = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReglaId = table.Column<int>(type: "int", nullable: true),
                    ReglaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    BasesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimientosOperativos", x => x.Id);
                    table.UniqueConstraint("AK_MovimientosOperativos_ClienteAppId_Id", x => new { x.ClienteAppId, x.Id });
                    table.CheckConstraint("CK_Movimiento_Parada", "[ParadaEntregaId] IS NULL OR [OrdenEntregaId] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_MovimientosOperativos_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimientosOperativos_LiquidacionesOperativas_ClienteAppId_LiquidacionId",
                        columns: x => new { x.ClienteAppId, x.LiquidacionId },
                        principalTable: "LiquidacionesOperativas",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimientosOperativos_OrdenesEntrega_ClienteAppId_OrdenEntregaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId },
                        principalTable: "OrdenesEntrega",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimientosOperativos_ParadasEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId, x.ParadaEntregaId },
                        principalTable: "ParadasEntrega",
                        principalColumns: new[] { "ClienteAppId", "OrdenEntregaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimientosOperativos_ReglasRemuneracion_ClienteAppId_ReglaId",
                        columns: x => new { x.ClienteAppId, x.ReglaId },
                        principalTable: "ReglasRemuneracion",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimientosOperativos_RutasOperativas_ClienteAppId_RutaId",
                        columns: x => new { x.ClienteAppId, x.RutaId },
                        principalTable: "RutasOperativas",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RutaOrdenesOperativas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    RutaId = table.Column<int>(type: "int", nullable: false),
                    OrdenEntregaId = table.Column<int>(type: "int", nullable: false),
                    Vigente = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RutaOrdenesOperativas", x => x.Id);
                    table.UniqueConstraint("AK_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId", x => new { x.ClienteAppId, x.RutaId, x.OrdenEntregaId });
                    table.ForeignKey(
                        name: "FK_RutaOrdenesOperativas_OrdenesEntrega_ClienteAppId_OrdenEntregaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId },
                        principalTable: "OrdenesEntrega",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RutaOrdenesOperativas_RutasOperativas_ClienteAppId_RutaId",
                        columns: x => new { x.ClienteAppId, x.RutaId },
                        principalTable: "RutasOperativas",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActividadesIncidencia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    IncidenciaId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Accion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Texto = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActividadesIncidencia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActividadesIncidencia_IncidenciasOperativas_ClienteAppId_IncidenciaId",
                        columns: x => new { x.ClienteAppId, x.IncidenciaId },
                        principalTable: "IncidenciasOperativas",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PuntosRutaOperativos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    RutaId = table.Column<int>(type: "int", nullable: false),
                    OrdenEntregaId = table.Column<int>(type: "int", nullable: false),
                    RutaOrdenId = table.Column<int>(type: "int", nullable: false),
                    ParadaEntregaId = table.Column<int>(type: "int", nullable: true),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Posicion = table.Column<int>(type: "int", nullable: false),
                    LlegadaAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PuntosRutaOperativos", x => x.Id);
                    table.CheckConstraint("CK_PuntoRuta_Tipo", "([Tipo] = 1 AND [ParadaEntregaId] IS NULL) OR ([Tipo] = 2 AND [ParadaEntregaId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PuntosRutaOperativos_ParadasEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                        columns: x => new { x.ClienteAppId, x.OrdenEntregaId, x.ParadaEntregaId },
                        principalTable: "ParadasEntrega",
                        principalColumns: new[] { "ClienteAppId", "OrdenEntregaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PuntosRutaOperativos_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId",
                        columns: x => new { x.ClienteAppId, x.RutaId, x.OrdenEntregaId },
                        principalTable: "RutaOrdenesOperativas",
                        principalColumns: new[] { "ClienteAppId", "RutaId", "OrdenEntregaId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PuntosRutaOperativos_RutasOperativas_ClienteAppId_RutaId",
                        columns: x => new { x.ClienteAppId, x.RutaId },
                        principalTable: "RutasOperativas",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodigosEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "CodigosEntrega",
                columns: new[] { "ClienteAppId", "OrdenEntregaId", "ParadaEntregaId" });

            migrationBuilder.CreateIndex(
                name: "IX_CodigosEntrega_OrdenEntregaId_Tipo",
                table: "CodigosEntrega",
                columns: new[] { "OrdenEntregaId", "Tipo" },
                unique: true,
                filter: "[ParadaEntregaId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CodigosEntrega_ParadaEntregaId",
                table: "CodigosEntrega",
                column: "ParadaEntregaId",
                unique: true,
                filter: "[ParadaEntregaId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActividadesIncidencia_ClienteAppId_IncidenciaId",
                table: "ActividadesIncidencia",
                columns: new[] { "ClienteAppId", "IncidenciaId" });

            migrationBuilder.CreateIndex(
                name: "IX_CobrosOperativos_ClienteAppId_LiquidacionId",
                table: "CobrosOperativos",
                columns: new[] { "ClienteAppId", "LiquidacionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CobrosOperativos_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "CobrosOperativos",
                columns: new[] { "ClienteAppId", "OrdenEntregaId", "ParadaEntregaId" });

            migrationBuilder.CreateIndex(
                name: "IX_CobrosOperativos_ClienteAppId_PilotoId",
                table: "CobrosOperativos",
                columns: new[] { "ClienteAppId", "PilotoId" });

            migrationBuilder.CreateIndex(
                name: "IX_CobrosOperativos_ClienteAppId_Referencia",
                table: "CobrosOperativos",
                columns: new[] { "ClienteAppId", "Referencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CobrosOperativos_ClienteAppId_TiendaId",
                table: "CobrosOperativos",
                columns: new[] { "ClienteAppId", "TiendaId" });

            migrationBuilder.CreateIndex(
                name: "IX_DatosEnviosOperativos_ClienteAppId_OrdenEntregaId",
                table: "DatosEnviosOperativos",
                columns: new[] { "ClienteAppId", "OrdenEntregaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventosOperativos_ClienteAppId",
                table: "EventosOperativos",
                column: "ClienteAppId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidenciasOperativas_ClienteAppId_ClaveDeteccion",
                table: "IncidenciasOperativas",
                columns: new[] { "ClienteAppId", "ClaveDeteccion" },
                unique: true,
                filter: "[ClaveDeteccion] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IncidenciasOperativas_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "IncidenciasOperativas",
                columns: new[] { "ClienteAppId", "OrdenEntregaId", "ParadaEntregaId" });

            migrationBuilder.CreateIndex(
                name: "IX_IncidenciasOperativas_ClienteAppId_RutaId",
                table: "IncidenciasOperativas",
                columns: new[] { "ClienteAppId", "RutaId" });

            migrationBuilder.CreateIndex(
                name: "IX_LiquidacionesOperativas_ClienteAppId_PilotoId",
                table: "LiquidacionesOperativas",
                columns: new[] { "ClienteAppId", "PilotoId" });

            migrationBuilder.CreateIndex(
                name: "IX_LiquidacionesOperativas_ClienteAppId_TiendaId",
                table: "LiquidacionesOperativas",
                columns: new[] { "ClienteAppId", "TiendaId" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosOperativos_ClienteAppId_ClaveOrigen",
                table: "MovimientosOperativos",
                columns: new[] { "ClienteAppId", "ClaveOrigen" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosOperativos_ClienteAppId_LiquidacionId",
                table: "MovimientosOperativos",
                columns: new[] { "ClienteAppId", "LiquidacionId" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosOperativos_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "MovimientosOperativos",
                columns: new[] { "ClienteAppId", "OrdenEntregaId", "ParadaEntregaId" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosOperativos_ClienteAppId_ReglaId",
                table: "MovimientosOperativos",
                columns: new[] { "ClienteAppId", "ReglaId" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosOperativos_ClienteAppId_RutaId",
                table: "MovimientosOperativos",
                columns: new[] { "ClienteAppId", "RutaId" });

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesEnvirapido_ClienteAppId_ActorId_Tipo_Clave",
                table: "OperacionesEnvirapido",
                columns: new[] { "ClienteAppId", "ActorId", "Tipo", "Clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParadasEntrega_ClienteAppId_OrdenEntregaId_TiendaId",
                table: "ParadasEntrega",
                columns: new[] { "ClienteAppId", "OrdenEntregaId", "TiendaId" });

            migrationBuilder.CreateIndex(
                name: "IX_ParadasEntrega_OrdenEntregaId_Posicion",
                table: "ParadasEntrega",
                columns: new[] { "OrdenEntregaId", "Posicion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParadasEntrega_TiendaId_UsuarioFinalId",
                table: "ParadasEntrega",
                columns: new[] { "TiendaId", "UsuarioFinalId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosicionesPilotosOperativas_ClienteAppId_PilotoId_CapturadoAt",
                table: "PosicionesPilotosOperativas",
                columns: new[] { "ClienteAppId", "PilotoId", "CapturadoAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PosicionesPilotosOperativas_ClienteAppId_PilotoId_DispositivoId_Secuencia",
                table: "PosicionesPilotosOperativas",
                columns: new[] { "ClienteAppId", "PilotoId", "DispositivoId", "Secuencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PuntosRutaOperativos_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "PuntosRutaOperativos",
                columns: new[] { "ClienteAppId", "OrdenEntregaId", "ParadaEntregaId" });

            migrationBuilder.CreateIndex(
                name: "IX_PuntosRutaOperativos_ClienteAppId_RutaId_OrdenEntregaId",
                table: "PuntosRutaOperativos",
                columns: new[] { "ClienteAppId", "RutaId", "OrdenEntregaId" });

            migrationBuilder.CreateIndex(
                name: "IX_PuntosRutaOperativos_RutaId_Posicion",
                table: "PuntosRutaOperativos",
                columns: new[] { "RutaId", "Posicion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReglasIncidencia_ClienteAppId_Categoria",
                table: "ReglasIncidencia",
                columns: new[] { "ClienteAppId", "Categoria" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RutaOrdenesOperativas_ClienteAppId_OrdenEntregaId",
                table: "RutaOrdenesOperativas",
                columns: new[] { "ClienteAppId", "OrdenEntregaId" },
                unique: true,
                filter: "[Vigente] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_RutasOperativas_ClienteAppId_PilotoId",
                table: "RutasOperativas",
                columns: new[] { "ClienteAppId", "PilotoId" });

            migrationBuilder.CreateIndex(
                name: "IX_RutasOperativas_ClienteAppId_ZonaId",
                table: "RutasOperativas",
                columns: new[] { "ClienteAppId", "ZonaId" });

            migrationBuilder.CreateIndex(
                name: "IX_ZonasOperativas_ClienteAppId_Nombre",
                table: "ZonasOperativas",
                columns: new[] { "ClienteAppId", "Nombre" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CodigosEntrega_OrdenesEntrega_ClienteAppId_OrdenEntregaId",
                table: "CodigosEntrega",
                columns: new[] { "ClienteAppId", "OrdenEntregaId" },
                principalTable: "OrdenesEntrega",
                principalColumns: new[] { "ClienteAppId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CodigosEntrega_ParadasEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "CodigosEntrega",
                columns: new[] { "ClienteAppId", "OrdenEntregaId", "ParadaEntregaId" },
                principalTable: "ParadasEntrega",
                principalColumns: new[] { "ClienteAppId", "OrdenEntregaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // Materializar una única entrega histórica sin regenerar ningún PIN ni ID.
            migrationBuilder.Sql(@"
                INSERT INTO [ParadasEntrega] ([ClienteAppId],[OrdenEntregaId],[UsuarioFinalId],[TiendaId],
                    [Posicion],[Nombre],[Direccion],[Telefono],[MapaLink],[Latitud],[Longitud],
                    [Instrucciones],[Estado],[EntregadaAt],[CreatedAt])
                SELECT o.[ClienteAppId],o.[Id],o.[UsuarioFinalId],o.[TiendaId],1,
                    CASE WHEN o.[DatosEntregaCongelados]=1 THEN o.[DestinoNombre] ELSE u.[Nombre] END,
                    CASE WHEN o.[DatosEntregaCongelados]=1 THEN o.[DestinoDireccion] ELSE u.[DireccionUbicacion] END,
                    CASE WHEN o.[DatosEntregaCongelados]=1 THEN o.[DestinoTelefono] ELSE u.[Telefono] END,
                    CASE WHEN o.[DatosEntregaCongelados]=1 THEN o.[DestinoMapaLink] ELSE u.[MapaLink] END,
                    CASE WHEN o.[DatosEntregaCongelados]=1 THEN o.[DestinoLatitud] ELSE u.[Latitud] END,
                    CASE WHEN o.[DatosEntregaCongelados]=1 THEN o.[DestinoLongitud] ELSE u.[Longitud] END,
                    NULL,CASE WHEN o.[Estado]=4 THEN 2 ELSE 1 END,o.[EntregadaAt],o.[CreatedAt]
                FROM [OrdenesEntrega] o INNER JOIN [UsuariosFinales] u ON u.[Id]=o.[UsuarioFinalId]
                    AND u.[TiendaId]=o.[TiendaId]
                WHERE NOT EXISTS(SELECT 1 FROM [ParadasEntrega] p WHERE p.[OrdenEntregaId]=o.[Id]);
                IF EXISTS(SELECT 1 FROM [OrdenesEntrega] o WHERE NOT EXISTS
                    (SELECT 1 FROM [ParadasEntrega] p WHERE p.[OrdenEntregaId]=o.[Id]))
                    THROW 51010, 'Existen órdenes sin destinatario histórico coherente; revisar antes de migrar.', 1;
                UPDATE c SET c.[ParadaEntregaId]=p.[Id]
                FROM [CodigosEntrega] c INNER JOIN [ParadasEntrega] p
                    ON p.[OrdenEntregaId]=c.[OrdenEntregaId] AND p.[ClienteAppId]=c.[ClienteAppId]
                WHERE c.[Tipo]=2 AND c.[ParadaEntregaId] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS(SELECT 1 FROM [ParadasEntrega]) OR EXISTS(SELECT 1 FROM [RutasOperativas])
                    OR EXISTS(SELECT 1 FROM [ZonasOperativas]) OR EXISTS(SELECT 1 FROM [IncidenciasOperativas])
                    OR EXISTS(SELECT 1 FROM [LiquidacionesOperativas]) OR EXISTS(SELECT 1 FROM [MovimientosOperativos])
                    OR EXISTS(SELECT 1 FROM [CobrosOperativos]) OR EXISTS(SELECT 1 FROM [ReglasIncidencia])
                    OR EXISTS(SELECT 1 FROM [ReglasRemuneracion]) OR EXISTS(SELECT 1 FROM [DatosEnviosOperativos])
                    OR EXISTS(SELECT 1 FROM [PosicionesPilotosOperativas]) OR EXISTS(SELECT 1 FROM [EventosOperativos])
                    OR EXISTS(SELECT 1 FROM [OperacionesEnvirapido])
                    THROW 51011, 'Rollback destructivo bloqueado: hay paradas o datos operativos. Usar restauración aprobada.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_CodigosEntrega_OrdenesEntrega_ClienteAppId_OrdenEntregaId",
                table: "CodigosEntrega");

            migrationBuilder.DropForeignKey(
                name: "FK_CodigosEntrega_ParadasEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "CodigosEntrega");

            migrationBuilder.DropTable(
                name: "ActividadesIncidencia");

            migrationBuilder.DropTable(
                name: "CobrosOperativos");

            migrationBuilder.DropTable(
                name: "DatosEnviosOperativos");

            migrationBuilder.DropTable(
                name: "EventosOperativos");

            migrationBuilder.DropTable(
                name: "MovimientosOperativos");

            migrationBuilder.DropTable(
                name: "OperacionesEnvirapido");

            migrationBuilder.DropTable(
                name: "PosicionesPilotosOperativas");

            migrationBuilder.DropTable(
                name: "PuntosRutaOperativos");

            migrationBuilder.DropTable(
                name: "ReglasIncidencia");

            migrationBuilder.DropTable(
                name: "IncidenciasOperativas");

            migrationBuilder.DropTable(
                name: "LiquidacionesOperativas");

            migrationBuilder.DropTable(
                name: "ReglasRemuneracion");

            migrationBuilder.DropTable(
                name: "RutaOrdenesOperativas");

            migrationBuilder.DropTable(
                name: "ParadasEntrega");

            migrationBuilder.DropTable(
                name: "RutasOperativas");

            migrationBuilder.DropTable(
                name: "ZonasOperativas");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_UsuariosFinales_TiendaId_Id",
                table: "UsuariosFinales");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Tiendas_ClienteAppId_Id",
                table: "Tiendas");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Pilotos_ClienteAppId_Id",
                table: "Pilotos");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrdenesEntrega_ClienteAppId_Id",
                table: "OrdenesEntrega");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrdenesEntrega_ClienteAppId_Id_TiendaId",
                table: "OrdenesEntrega");

            migrationBuilder.DropIndex(
                name: "IX_CodigosEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId",
                table: "CodigosEntrega");

            migrationBuilder.DropIndex(
                name: "IX_CodigosEntrega_OrdenEntregaId_Tipo",
                table: "CodigosEntrega");

            migrationBuilder.DropIndex(
                name: "IX_CodigosEntrega_ParadaEntregaId",
                table: "CodigosEntrega");

            migrationBuilder.DropColumn(
                name: "ResultadoJson",
                table: "OperacionesOrden");

            migrationBuilder.DropColumn(
                name: "ParadaEntregaId",
                table: "CodigosEntrega");

            migrationBuilder.AlterColumn<int>(
                name: "UsuarioFinalId",
                table: "OrdenesEntrega",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosFinales_TiendaId",
                table: "UsuariosFinales",
                column: "TiendaId");

            migrationBuilder.CreateIndex(
                name: "IX_Tiendas_ClienteAppId",
                table: "Tiendas",
                column: "ClienteAppId");

            migrationBuilder.CreateIndex(
                name: "IX_Pilotos_ClienteAppId",
                table: "Pilotos",
                column: "ClienteAppId");

            migrationBuilder.CreateIndex(
                name: "IX_CodigosEntrega_OrdenEntregaId_Tipo",
                table: "CodigosEntrega",
                columns: new[] { "OrdenEntregaId", "Tipo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CodigosEntrega_OrdenesEntrega_OrdenEntregaId",
                table: "CodigosEntrega",
                column: "OrdenEntregaId",
                principalTable: "OrdenesEntrega",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
