BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN

    IF EXISTS (SELECT 1 FROM PuntosRutaOperativos p WHERE NOT EXISTS
        (SELECT 1 FROM RutaOrdenesOperativas r WHERE r.ClienteAppId=p.ClienteAppId AND r.RutaId=p.RutaId AND r.OrdenEntregaId=p.OrdenEntregaId))
        THROW 51001, 'Existen visitas sin membresia de ruta; revisar antes de migrar.', 1;
    UPDATE p SET RutaOrdenId=r.Id FROM PuntosRutaOperativos p
    INNER JOIN RutaOrdenesOperativas r ON r.ClienteAppId=p.ClienteAppId AND r.RutaId=p.RutaId AND r.OrdenEntregaId=p.OrdenEntregaId;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [PuntosRutaOperativos] DROP CONSTRAINT [FK_PuntosRutaOperativos_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [RutaOrdenesOperativas] DROP CONSTRAINT [AK_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    DROP INDEX [IX_RutaOrdenesOperativas_ClienteAppId_OrdenEntregaId] ON [RutaOrdenesOperativas];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    DROP INDEX [IX_PuntosRutaOperativos_ClienteAppId_RutaId_OrdenEntregaId] ON [PuntosRutaOperativos];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [PuntosRutaOperativos] DROP CONSTRAINT [CK_PuntoRuta_Tipo];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [RutaOrdenesOperativas] ADD [ParadaEntregaId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [PuntosRutaOperativos] ADD [RecogidaConfirmadaAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [ParadasEntrega] ADD [EnReasignacion] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [ParadasEntrega] ADD [PilotoAsignadoId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [ParadasEntrega] ADD [PilotoCustodioId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [ParadasEntrega] ADD [PilotoEntregaId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [ParadasEntrega] ADD [RecogidaActualDireccion] nvarchar(250) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [ParadasEntrega] ADD [RecogidaActualLatitud] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [ParadasEntrega] ADD [RecogidaActualLongitud] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [ParadasEntrega] ADD [RecogidaActualNombre] nvarchar(120) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [ParadasEntrega] ADD [RecogidaParadaAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [ParadasEntrega] ADD [RevisionAsignacion] nvarchar(32) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [GestionPorParadas] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [RutaOrdenesOperativas] ADD CONSTRAINT [AK_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId_Id] UNIQUE ([ClienteAppId], [RutaId], [OrdenEntregaId], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    CREATE TABLE [RetirosParadas] (
        [Id] int NOT NULL IDENTITY,
        [OrdenEntregaId] int NOT NULL,
        [ParadaEntregaId] int NOT NULL,
        [PilotoOrigenId] int NULL,
        [PilotoDestinoId] int NULL,
        [TiendaDestinoId] int NULL,
        [IncidenciaId] int NULL,
        [Estado] nvarchar(32) NOT NULL,
        [TipoDestino] nvarchar(16) NULL,
        [DestinoNombre] nvarchar(120) NULL,
        [DestinoDireccion] nvarchar(250) NULL,
        [DestinoLatitud] float NULL,
        [DestinoLongitud] float NULL,
        [Motivo] nvarchar(1000) NOT NULL,
        [NotaDecision] nvarchar(1000) NULL,
        [SolicitadaPor] nvarchar(450) NOT NULL,
        [DecididaPor] nvarchar(450) NULL,
        [RecibidaPor] nvarchar(450) NULL,
        [RecibidaAt] datetime2 NULL,
        [Abierta] bit NOT NULL,
        [ClienteAppId] int NOT NULL,
        [Version] nvarchar(32) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_RetirosParadas] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_RetirosParadas_ClienteAppId_Id] UNIQUE ([ClienteAppId], [Id]),
        CONSTRAINT [CK_Retiro_Estado] CHECK ([Estado] IN ('Solicitada','Rechazada','PorReasignar','PendienteRecepcion','Recibida','Reasignada','Cancelada')),
        CONSTRAINT [FK_RetirosParadas_ClientesApp_ClienteAppId] FOREIGN KEY ([ClienteAppId]) REFERENCES [ClientesApp] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RetirosParadas_IncidenciasOperativas_ClienteAppId_IncidenciaId] FOREIGN KEY ([ClienteAppId], [IncidenciaId]) REFERENCES [IncidenciasOperativas] ([ClienteAppId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RetirosParadas_OrdenesEntrega_ClienteAppId_OrdenEntregaId] FOREIGN KEY ([ClienteAppId], [OrdenEntregaId]) REFERENCES [OrdenesEntrega] ([ClienteAppId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RetirosParadas_ParadasEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId] FOREIGN KEY ([ClienteAppId], [OrdenEntregaId], [ParadaEntregaId]) REFERENCES [ParadasEntrega] ([ClienteAppId], [OrdenEntregaId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RetirosParadas_Pilotos_ClienteAppId_PilotoDestinoId] FOREIGN KEY ([ClienteAppId], [PilotoDestinoId]) REFERENCES [Pilotos] ([ClienteAppId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RetirosParadas_Pilotos_ClienteAppId_PilotoOrigenId] FOREIGN KEY ([ClienteAppId], [PilotoOrigenId]) REFERENCES [Pilotos] ([ClienteAppId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RetirosParadas_Tiendas_ClienteAppId_TiendaDestinoId] FOREIGN KEY ([ClienteAppId], [TiendaDestinoId]) REFERENCES [Tiendas] ([ClienteAppId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RutaOrdenesOperativas_ClienteAppId_OrdenEntregaId] ON [RutaOrdenesOperativas] ([ClienteAppId], [OrdenEntregaId]) WHERE [Vigente] = 1 AND [ParadaEntregaId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    CREATE INDEX [IX_RutaOrdenesOperativas_ClienteAppId_OrdenEntregaId_ParadaEntregaId] ON [RutaOrdenesOperativas] ([ClienteAppId], [OrdenEntregaId], [ParadaEntregaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RutaOrdenesOperativas_ClienteAppId_ParadaEntregaId] ON [RutaOrdenesOperativas] ([ClienteAppId], [ParadaEntregaId]) WHERE [Vigente] = 1 AND [ParadaEntregaId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    CREATE INDEX [IX_PuntosRutaOperativos_ClienteAppId_RutaId_OrdenEntregaId_RutaOrdenId] ON [PuntosRutaOperativos] ([ClienteAppId], [RutaId], [OrdenEntregaId], [RutaOrdenId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    EXEC(N'ALTER TABLE [PuntosRutaOperativos] ADD CONSTRAINT [CK_PuntoRuta_Tipo] CHECK ([Tipo] = 1 OR ([Tipo] IN (2, 3) AND [ParadaEntregaId] IS NOT NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    CREATE INDEX [IX_ParadasEntrega_ClienteAppId_PilotoAsignadoId] ON [ParadasEntrega] ([ClienteAppId], [PilotoAsignadoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    CREATE INDEX [IX_ParadasEntrega_ClienteAppId_PilotoCustodioId] ON [ParadasEntrega] ([ClienteAppId], [PilotoCustodioId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    CREATE INDEX [IX_ParadasEntrega_ClienteAppId_PilotoEntregaId] ON [ParadasEntrega] ([ClienteAppId], [PilotoEntregaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    CREATE INDEX [IX_RetirosParadas_ClienteAppId_IncidenciaId] ON [RetirosParadas] ([ClienteAppId], [IncidenciaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    CREATE INDEX [IX_RetirosParadas_ClienteAppId_OrdenEntregaId_ParadaEntregaId] ON [RetirosParadas] ([ClienteAppId], [OrdenEntregaId], [ParadaEntregaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RetirosParadas_ClienteAppId_ParadaEntregaId] ON [RetirosParadas] ([ClienteAppId], [ParadaEntregaId]) WHERE [Abierta] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    CREATE INDEX [IX_RetirosParadas_ClienteAppId_PilotoDestinoId] ON [RetirosParadas] ([ClienteAppId], [PilotoDestinoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    CREATE INDEX [IX_RetirosParadas_ClienteAppId_PilotoOrigenId] ON [RetirosParadas] ([ClienteAppId], [PilotoOrigenId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    CREATE INDEX [IX_RetirosParadas_ClienteAppId_TiendaDestinoId] ON [RetirosParadas] ([ClienteAppId], [TiendaDestinoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [ParadasEntrega] ADD CONSTRAINT [FK_ParadasEntrega_Pilotos_ClienteAppId_PilotoAsignadoId] FOREIGN KEY ([ClienteAppId], [PilotoAsignadoId]) REFERENCES [Pilotos] ([ClienteAppId], [Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [ParadasEntrega] ADD CONSTRAINT [FK_ParadasEntrega_Pilotos_ClienteAppId_PilotoCustodioId] FOREIGN KEY ([ClienteAppId], [PilotoCustodioId]) REFERENCES [Pilotos] ([ClienteAppId], [Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [ParadasEntrega] ADD CONSTRAINT [FK_ParadasEntrega_Pilotos_ClienteAppId_PilotoEntregaId] FOREIGN KEY ([ClienteAppId], [PilotoEntregaId]) REFERENCES [Pilotos] ([ClienteAppId], [Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [PuntosRutaOperativos] ADD CONSTRAINT [FK_PuntosRutaOperativos_RutaOrdenesOperativas_ClienteAppId_RutaId_OrdenEntregaId_RutaOrdenId] FOREIGN KEY ([ClienteAppId], [RutaId], [OrdenEntregaId], [RutaOrdenId]) REFERENCES [RutaOrdenesOperativas] ([ClienteAppId], [RutaId], [OrdenEntregaId], [Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    ALTER TABLE [RutaOrdenesOperativas] ADD CONSTRAINT [FK_RutaOrdenesOperativas_ParadasEntrega_ClienteAppId_OrdenEntregaId_ParadaEntregaId] FOREIGN KEY ([ClienteAppId], [OrdenEntregaId], [ParadaEntregaId]) REFERENCES [ParadasEntrega] ([ClienteAppId], [OrdenEntregaId], [Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117230916_EnvirapidoReasignacionPorParadas'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261117230916_EnvirapidoReasignacionPorParadas', N'10.0.15');
END;

COMMIT;
GO

