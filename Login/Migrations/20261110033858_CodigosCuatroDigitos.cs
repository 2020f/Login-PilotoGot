using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Login.Migrations
{
    /// <inheritdoc />
    public partial class CodigosCuatroDigitos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CodigosEntrega_Codigo",
                table: "CodigosEntrega");

            migrationBuilder.AddColumn<string>(
                name: "CodigoA",
                table: "OrdenesEntrega",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClienteAppId",
                table: "CodigosEntrega",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Reservado",
                table: "CodigosEntrega",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Conservar literalmente los códigos emitidos. Solo completar su tenant
            // y la reserva; CodigoA queda NULL para que se siga mostrando el A anterior.
            migrationBuilder.Sql(@"
                UPDATE [CodigosEntrega]
                SET [ClienteAppId] = (SELECT [ClienteAppId] FROM [OrdenesEntrega]
                                     WHERE [Id] = [CodigosEntrega].[OrdenEntregaId]),
                    [Reservado] = CASE WHEN (SELECT [Estado] FROM [OrdenesEntrega]
                                     WHERE [Id] = [CodigosEntrega].[OrdenEntregaId]) = 4
                                     THEN 0 ELSE 1 END;");

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesEntrega_ClienteAppId_CodigoA",
                table: "OrdenesEntrega",
                columns: new[] { "ClienteAppId", "CodigoA" },
                unique: true,
                filter: "[CodigoA] IS NOT NULL AND [Estado] <> 4");

            migrationBuilder.CreateIndex(
                name: "IX_CodigosEntrega_ClienteAppId_Tipo_Codigo",
                table: "CodigosEntrega",
                columns: new[] { "ClienteAppId", "Tipo", "Codigo" },
                unique: true,
                filter: "[Reservado] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CodigosEntrega_Codigo",
                table: "CodigosEntrega",
                column: "Codigo");

            migrationBuilder.AddForeignKey(
                name: "FK_CodigosEntrega_ClientesApp_ClienteAppId",
                table: "CodigosEntrega",
                column: "ClienteAppId",
                principalTable: "ClientesApp",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Una vez emitidos códigos nuevos, restaurar la copia previa es el único
            // rollback que no cambia códigos ni pierde la relación con su historial.
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM [OrdenesEntrega] WHERE [CodigoA] IS NOT NULL)
                    THROW 51000, 'No se puede revertir después de emitir códigos de cuatro dígitos. Restaurar la copia previa.', 1;");

            migrationBuilder.DropForeignKey(
                name: "FK_CodigosEntrega_ClientesApp_ClienteAppId",
                table: "CodigosEntrega");

            migrationBuilder.DropIndex(
                name: "IX_OrdenesEntrega_ClienteAppId_CodigoA",
                table: "OrdenesEntrega");

            migrationBuilder.DropIndex(
                name: "IX_CodigosEntrega_ClienteAppId_Tipo_Codigo",
                table: "CodigosEntrega");

            migrationBuilder.DropIndex(
                name: "IX_CodigosEntrega_Codigo",
                table: "CodigosEntrega");

            migrationBuilder.DropColumn(
                name: "CodigoA",
                table: "OrdenesEntrega");

            migrationBuilder.DropColumn(
                name: "ClienteAppId",
                table: "CodigosEntrega");

            migrationBuilder.DropColumn(
                name: "Reservado",
                table: "CodigosEntrega");

            migrationBuilder.CreateIndex(
                name: "IX_CodigosEntrega_Codigo",
                table: "CodigosEntrega",
                column: "Codigo",
                unique: true);
        }
    }
}
