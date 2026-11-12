using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Login.Migrations
{
    /// <inheritdoc />
    public partial class EnvirapidoSaasBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SaasAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteAppId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Entity = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Before = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    After = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Actor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaasAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaasAuditLogs_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaasInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    Concept = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "date", nullable: false),
                    DueDate = table.Column<DateTime>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaasInvoices", x => x.Id);
                    table.UniqueConstraint("AK_SaasInvoices_ClienteAppId_Id", x => new { x.ClienteAppId, x.Id });
                    table.CheckConstraint("CK_SaasInvoice_AmountPeriod", "[Amount] > 0 AND [PeriodEnd] >= [PeriodStart]");
                    table.ForeignKey(
                        name: "FK_SaasInvoices_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaasPlanRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    RequestedPlanId = table.Column<int>(type: "int", nullable: false),
                    PreviousPlanId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ResolutionKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ResolutionResetOverrides = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaasPlanRequests", x => x.Id);
                    table.CheckConstraint("CK_SaasPlanRequest_Status", "[Status] IN ('Pendiente','Aprobada','Rechazada')");
                    table.ForeignKey(
                        name: "FK_SaasPlanRequests_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaasPlanRequests_Planes_RequestedPlanId",
                        column: x => x.RequestedPlanId,
                        principalTable: "Planes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaasSubscriptions",
                columns: table => new
                {
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    BaselineStart = table.Column<DateTime>(type: "date", nullable: true),
                    BaselineEnd = table.Column<DateTime>(type: "date", nullable: true),
                    BaselineTrial = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaasSubscriptions", x => x.ClienteAppId);
                    table.ForeignKey(
                        name: "FK_SaasSubscriptions_ClientesApp_ClienteAppId",
                        column: x => x.ClienteAppId,
                        principalTable: "ClientesApp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaasPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteAppId = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidOn = table.Column<DateTime>(type: "date", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReferenceKey = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaasPayments", x => x.Id);
                    table.CheckConstraint("CK_SaasPayment_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_SaasPayments_SaasInvoices_ClienteAppId_InvoiceId",
                        columns: x => new { x.ClienteAppId, x.InvoiceId },
                        principalTable: "SaasInvoices",
                        principalColumns: new[] { "ClienteAppId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaasAuditLogs_ClienteAppId_Action_OperationKey",
                table: "SaasAuditLogs",
                columns: new[] { "ClienteAppId", "Action", "OperationKey" },
                unique: true,
                filter: "[OperationKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SaasAuditLogs_ClienteAppId_CreatedAt",
                table: "SaasAuditLogs",
                columns: new[] { "ClienteAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SaasInvoices_ClienteAppId_DueDate",
                table: "SaasInvoices",
                columns: new[] { "ClienteAppId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SaasInvoices_ClienteAppId_IdempotencyKey",
                table: "SaasInvoices",
                columns: new[] { "ClienteAppId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaasPayments_ClienteAppId_IdempotencyKey",
                table: "SaasPayments",
                columns: new[] { "ClienteAppId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaasPayments_ClienteAppId_InvoiceId",
                table: "SaasPayments",
                columns: new[] { "ClienteAppId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SaasPayments_ClienteAppId_ReferenceKey",
                table: "SaasPayments",
                columns: new[] { "ClienteAppId", "ReferenceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaasPlanRequests_ClienteAppId",
                table: "SaasPlanRequests",
                column: "ClienteAppId",
                unique: true,
                filter: "[Status] = 'Pendiente'");

            migrationBuilder.CreateIndex(
                name: "IX_SaasPlanRequests_ClienteAppId_IdempotencyKey",
                table: "SaasPlanRequests",
                columns: new[] { "ClienteAppId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaasPlanRequests_RequestedPlanId",
                table: "SaasPlanRequests",
                column: "RequestedPlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
                migrationBuilder.Sql(@"IF EXISTS(SELECT 1 FROM [SaasInvoices]) OR EXISTS(SELECT 1 FROM [SaasPayments])
                    OR EXISTS(SELECT 1 FROM [SaasPlanRequests]) OR EXISTS(SELECT 1 FROM [SaasAuditLogs])
                    OR EXISTS(SELECT 1 FROM [SaasSubscriptions])
                    THROW 51000, 'Rollback bloqueado: existen registros SaaS de facturación, suscripción o auditoría. Conservar la base y restaurar una versión compatible.', 1;");
            migrationBuilder.DropTable(
                name: "SaasAuditLogs");

            migrationBuilder.DropTable(
                name: "SaasPayments");

            migrationBuilder.DropTable(
                name: "SaasPlanRequests");

            migrationBuilder.DropTable(
                name: "SaasSubscriptions");

            migrationBuilder.DropTable(
                name: "SaasInvoices");
        }
    }
}
