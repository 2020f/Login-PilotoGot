BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110033858_CodigosCuatroDigitos'
)
BEGIN
    DROP INDEX [IX_CodigosEntrega_Codigo] ON [CodigosEntrega];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110033858_CodigosCuatroDigitos'
)
BEGIN
    ALTER TABLE [OrdenesEntrega] ADD [CodigoA] nvarchar(4) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110033858_CodigosCuatroDigitos'
)
BEGIN
    ALTER TABLE [CodigosEntrega] ADD [ClienteAppId] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110033858_CodigosCuatroDigitos'
)
BEGIN
    ALTER TABLE [CodigosEntrega] ADD [Reservado] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110033858_CodigosCuatroDigitos'
)
BEGIN

                    UPDATE [CodigosEntrega]
                    SET [ClienteAppId] = (SELECT [ClienteAppId] FROM [OrdenesEntrega]
                                         WHERE [Id] = [CodigosEntrega].[OrdenEntregaId]),
                        [Reservado] = CASE WHEN (SELECT [Estado] FROM [OrdenesEntrega]
                                         WHERE [Id] = [CodigosEntrega].[OrdenEntregaId]) = 4
                                         THEN 0 ELSE 1 END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110033858_CodigosCuatroDigitos'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OrdenesEntrega_ClienteAppId_CodigoA] ON [OrdenesEntrega] ([ClienteAppId], [CodigoA]) WHERE [CodigoA] IS NOT NULL AND [Estado] <> 4');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110033858_CodigosCuatroDigitos'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CodigosEntrega_ClienteAppId_Tipo_Codigo] ON [CodigosEntrega] ([ClienteAppId], [Tipo], [Codigo]) WHERE [Reservado] = 1');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110033858_CodigosCuatroDigitos'
)
BEGIN
    CREATE INDEX [IX_CodigosEntrega_Codigo] ON [CodigosEntrega] ([Codigo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110033858_CodigosCuatroDigitos'
)
BEGIN
    ALTER TABLE [CodigosEntrega] ADD CONSTRAINT [FK_CodigosEntrega_ClientesApp_ClienteAppId] FOREIGN KEY ([ClienteAppId]) REFERENCES [ClientesApp] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110033858_CodigosCuatroDigitos'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110033858_CodigosCuatroDigitos', N'8.0.23');
END;
GO

COMMIT;
GO
