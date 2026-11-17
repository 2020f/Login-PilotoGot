-- Portal comercial independiente. Generado sin ejecutar operaciones sobre ninguna base.
-- Aplicar explícitamente solo a una base EnvirapidoPortal; nunca a la base CRM.
IF DB_NAME() NOT LIKE N'EnvirapidoPortal%'
    THROW 51000, N'Este script solo se permite en una base independiente EnvirapidoPortal.', 1;
GO
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.OrdenesEntrega', N'U') IS NOT NULL
    THROW 51001, N'La base contiene tablas CRM; se rechaza la migración del portal.', 1;
GO
-- Repetir las guardas en cada lote mutante protege incluso a ejecutores que
-- continúan después de un error. Usar además sqlcmd -b y -d explícito.
IF DB_NAME() NOT LIKE N'EnvirapidoPortal%'
    THROW 51000, N'Este script solo se permite en una base independiente EnvirapidoPortal.', 1;
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.OrdenesEntrega', N'U') IS NOT NULL
    THROW 51001, N'La base contiene tablas CRM; se rechaza la migración del portal.', 1;
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

IF DB_NAME() NOT LIKE N'EnvirapidoPortal%'
    THROW 51000, N'Este script solo se permite en una base independiente EnvirapidoPortal.', 1;
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.OrdenesEntrega', N'U') IS NOT NULL
    THROW 51001, N'La base contiene tablas CRM; se rechaza la migración del portal.', 1;
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [AuditEvents] (
        [Id] bigint NOT NULL IDENTITY,
        [Actor] nvarchar(150) NOT NULL,
        [Action] nvarchar(80) NOT NULL,
        [Entity] nvarchar(80) NOT NULL,
        [EntityId] nvarchar(100) NOT NULL,
        [Detail] nvarchar(2000) NOT NULL,
        [CreatedAt] bigint NOT NULL,
        CONSTRAINT [PK_AuditEvents] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [CatalogEntries] (
        [Code] nvarchar(80) NOT NULL,
        [IsPublished] bit NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [SortOrder] int NOT NULL,
        [Featured] bit NOT NULL,
        [LastPlanSnapshotJson] nvarchar(max) NOT NULL,
        [Version] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_CatalogEntries] PRIMARY KEY ([Code])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [Operations] (
        [Id] bigint NOT NULL IDENTITY,
        [Scope] nvarchar(300) NOT NULL,
        [Key] nvarchar(40) NOT NULL,
        [Hash] nvarchar(max) NOT NULL,
        [ResponseJson] nvarchar(max) NOT NULL,
        [CreatedAt] bigint NOT NULL,
        CONSTRAINT [PK_Operations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [Outbox] (
        [Id] uniqueidentifier NOT NULL,
        [ProtectedMessage] nvarchar(max) NOT NULL,
        [CreatedAt] bigint NOT NULL,
        [NextAttemptAt] bigint NOT NULL,
        [ProcessingUntil] bigint NULL,
        [SentAt] bigint NULL,
        [Attempts] int NOT NULL,
        [LastError] nvarchar(max) NULL,
        CONSTRAINT [PK_Outbox] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [PortalArtifactOperations] (
        [Id] nvarchar(32) NOT NULL,
        [Fingerprint] nvarchar(64) NOT NULL,
        [ResponseJson] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PortalArtifactOperations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [PortalArtifacts] (
        [Id] uniqueidentifier NOT NULL,
        [OriginalFileName] nvarchar(160) NOT NULL,
        [State] nvarchar(24) NOT NULL,
        [VersionName] nvarchar(80) NULL,
        [VersionCode] int NULL,
        [Bytes] bigint NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [PackageName] nvarchar(160) NULL,
        [CertificateSha256] nvarchar(64) NULL,
        [ArchitecturesJson] nvarchar(1000) NOT NULL,
        [MinSdk] int NULL,
        [TargetSdk] int NULL,
        [Notes] nvarchar(4000) NOT NULL,
        [SourceCommit] nvarchar(40) NOT NULL,
        [PublicBaseUrl] nvarchar(200) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ValidatedAt] datetime2 NULL,
        [PublishedAt] datetime2 NULL,
        [WithdrawnAt] datetime2 NULL,
        [ValidationMessage] nvarchar(500) NULL,
        [Version] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_PortalArtifacts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [NormalizedEmail] nvarchar(256) NOT NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [SecurityStamp] nvarchar(max) NOT NULL,
        [EmailConfirmed] bit NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [FailedLogins] int NOT NULL,
        [LockedUntil] bigint NULL,
        [CreatedAt] bigint NOT NULL,
        [Version] nvarchar(max) NOT NULL,
        [TermsVersion] nvarchar(max) NOT NULL,
        [PrivacyVersion] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [PortalArtifactChannels] (
        [Id] nvarchar(40) NOT NULL,
        [CurrentArtifactId] uniqueidentifier NULL,
        [MaximumPublishedVersionCode] int NOT NULL,
        [Version] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_PortalArtifactChannels] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PortalArtifactChannels_PortalArtifacts_CurrentArtifactId] FOREIGN KEY ([CurrentArtifactId]) REFERENCES [PortalArtifacts] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [PortalDownloadEvents] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [ArtifactId] uniqueidentifier NOT NULL,
        [At] datetime2 NOT NULL,
        [RangeRequested] bit NOT NULL,
        CONSTRAINT [PK_PortalDownloadEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PortalDownloadEvents_PortalArtifacts_ArtifactId] FOREIGN KEY ([ArtifactId]) REFERENCES [PortalArtifacts] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [Profiles] (
        [UserId] uniqueidentifier NOT NULL,
        [ResponsibleName] nvarchar(max) NOT NULL,
        [BusinessName] nvarchar(max) NOT NULL,
        [Phone] nvarchar(max) NOT NULL,
        [Province] nvarchar(max) NOT NULL,
        [City] nvarchar(max) NOT NULL,
        [OperationType] nvarchar(max) NOT NULL,
        [EstimatedStores] int NULL,
        [EstimatedPilots] int NULL,
        [EstimatedOrdersPerMonth] int NULL,
        [Rnc] nvarchar(max) NULL,
        [BillingAddress] nvarchar(max) NULL,
        [Notes] nvarchar(max) NULL,
        CONSTRAINT [PK_Profiles] PRIMARY KEY ([UserId]),
        CONSTRAINT [FK_Profiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [Requests] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [PlanSnapshotJson] nvarchar(max) NOT NULL,
        [ProfileSnapshotJson] nvarchar(max) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(max) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [PublicMessage] nvarchar(max) NULL,
        [CreatedAt] bigint NOT NULL,
        [Version] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Requests] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Requests_UserId_Id] UNIQUE ([UserId], [Id]),
        CONSTRAINT [FK_Requests_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [Sessions] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Stamp] nvarchar(max) NOT NULL,
        [ExpiresAt] bigint NOT NULL,
        [RevokedAt] bigint NULL,
        CONSTRAINT [PK_Sessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Sessions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [Tokens] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Purpose] nvarchar(40) NOT NULL,
        [Hash] nvarchar(64) NOT NULL,
        [ExpiresAt] bigint NOT NULL,
        [UsedAt] bigint NULL,
        [Stamp] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Tokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Tokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [AccessGrants] (
        [UserId] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [StartsOn] date NOT NULL,
        [EndsOn] date NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [Reason] nvarchar(max) NOT NULL,
        [Version] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_AccessGrants] PRIMARY KEY ([UserId]),
        CONSTRAINT [FK_AccessGrants_Requests_UserId_RequestId] FOREIGN KEY ([UserId], [RequestId]) REFERENCES [Requests] ([UserId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AccessGrants_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [Approvals] (
        [Id] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [Decision] nvarchar(max) NOT NULL,
        [Message] nvarchar(max) NOT NULL,
        [Actor] nvarchar(max) NOT NULL,
        [CreatedAt] bigint NOT NULL,
        CONSTRAINT [PK_Approvals] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Approvals_Requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [Requests] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [PaymentReports] (
        [Id] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [PaidOn] date NOT NULL,
        [Method] nvarchar(max) NOT NULL,
        [Reference] nvarchar(max) NOT NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] bigint NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [Resolution] nvarchar(max) NULL,
        [ResolvedBy] nvarchar(max) NULL,
        [ResolvedAt] bigint NULL,
        [Version] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_PaymentReports] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_PaymentReports_RequestId_Id] UNIQUE ([RequestId], [Id]),
        CONSTRAINT [FK_PaymentReports_Requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [Requests] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE TABLE [Payments] (
        [Id] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [ReportId] uniqueidentifier NULL,
        [Amount] decimal(18,2) NOT NULL,
        [PaidOn] date NOT NULL,
        [Method] nvarchar(max) NOT NULL,
        [Reference] nvarchar(max) NOT NULL,
        [ReferenceKey] nvarchar(140) NOT NULL,
        [Actor] nvarchar(max) NOT NULL,
        [Reason] nvarchar(max) NOT NULL,
        [CreatedAt] bigint NOT NULL,
        [VoidedAt] bigint NULL,
        [VoidReason] nvarchar(max) NULL,
        [Version] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Payments_PaymentReports_RequestId_ReportId] FOREIGN KEY ([RequestId], [ReportId]) REFERENCES [PaymentReports] ([RequestId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Payments_Requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [Requests] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE INDEX [IX_AccessGrants_UserId_RequestId] ON [AccessGrants] ([UserId], [RequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE INDEX [IX_Approvals_RequestId] ON [Approvals] ([RequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Operations_Scope_Key] ON [Operations] ([Scope], [Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE INDEX [IX_Outbox_SentAt_NextAttemptAt] ON [Outbox] ([SentAt], [NextAttemptAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Payments_ReferenceKey] ON [Payments] ([ReferenceKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE INDEX [IX_Payments_RequestId_ReportId] ON [Payments] ([RequestId], [ReportId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE INDEX [IX_PortalArtifactChannels_CurrentArtifactId] ON [PortalArtifactChannels] ([CurrentArtifactId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PortalArtifacts_Sha256] ON [PortalArtifacts] ([Sha256]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE INDEX [IX_PortalDownloadEvents_ArtifactId] ON [PortalDownloadEvents] ([ArtifactId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE INDEX [IX_PortalDownloadEvents_UserId_At] ON [PortalDownloadEvents] ([UserId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Requests_UserId] ON [Requests] ([UserId]) WHERE [Status] IN (''Submitted'',''InReview'',''NeedsInformation'')');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE INDEX [IX_Sessions_UserId_ExpiresAt] ON [Sessions] ([UserId], [ExpiresAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tokens_UserId_Purpose_Hash] ON [Tokens] ([UserId], [Purpose], [Hash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_NormalizedEmail] ON [Users] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117144550_PortalInitial'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261117144550_PortalInitial', N'10.0.15');
END;

COMMIT;
GO
