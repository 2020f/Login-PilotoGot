-- Opciones requeridas por los índices filtrados, también al ejecutar con sqlcmd.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [DatosEntregaCongelados] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [DestinoDireccion] nvarchar(250) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [DestinoLatitud] float NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [DestinoLongitud] float NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [DestinoMapaLink] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [DestinoNombre] nvarchar(120) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [DestinoTelefono] nvarchar(30) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [RecogidaDireccion] nvarchar(200) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [RecogidaLatitud] float NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [RecogidaLongitud] float NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [RecogidaNombre] nvarchar(120) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [RecogidaTelefono] nvarchar(30) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN

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
                    WHERE o.[DatosEntregaCongelados] = 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    CREATE TABLE [OperacionesOrden] (
        [Id] int NOT NULL IDENTITY,
        [ClienteAppId] int NOT NULL,
        [ActorIdentityUserId] nvarchar(450) NOT NULL,
        [Tipo] nvarchar(30) NOT NULL,
        [Clave] nvarchar(64) NOT NULL,
        [PayloadHash] nvarchar(64) NOT NULL,
        [OrdenEntregaId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_OperacionesOrden] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OperacionesOrden_ClientesApp_ClienteAppId] FOREIGN KEY ([ClienteAppId]) REFERENCES [ClientesApp] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OperacionesOrden_OrdenesEntrega_OrdenEntregaId] FOREIGN KEY ([OrdenEntregaId]) REFERENCES [OrdenesEntrega] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OperacionesOrden_ClienteAppId_ActorIdentityUserId_Tipo_Clave] ON [OperacionesOrden] ([ClienteAppId], [ActorIdentityUserId], [Tipo], [Clave]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    CREATE INDEX [IX_OperacionesOrden_OrdenEntregaId] ON [OperacionesOrden] ([OrdenEntregaId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111222204_ConfiabilidadOrdenesSnapshot'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111222204_ConfiabilidadOrdenesSnapshot', N'8.0.23');
END;
GO

COMMIT;
GO
