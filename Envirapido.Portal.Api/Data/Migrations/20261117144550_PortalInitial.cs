using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Envirapido.Portal.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PortalInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF DB_NAME() NOT LIKE N'EnvirapidoPortal%'
                    THROW 51000, N'La migración requiere una base independiente EnvirapidoPortal.', 1;
                IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.OrdenesEntrega', N'U') IS NOT NULL
                    THROW 51001, N'La base contiene tablas CRM; se rechaza la migración del portal.', 1;
                """);
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Actor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Entity = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogEntries",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Featured = table.Column<bool>(type: "bit", nullable: false),
                    LastPlanSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogEntries", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Operations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Scope = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProtectedMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    NextAttemptAt = table.Column<long>(type: "bigint", nullable: false),
                    ProcessingUntil = table.Column<long>(type: "bigint", nullable: true),
                    SentAt = table.Column<long>(type: "bigint", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PortalArtifactOperations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalArtifactOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PortalArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    State = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    VersionName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    VersionCode = table.Column<int>(type: "int", nullable: true),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PackageName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    CertificateSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ArchitecturesJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    MinSdk = table.Column<int>(type: "int", nullable: true),
                    TargetSdk = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SourceCommit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PublicBaseUrl = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WithdrawnAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidationMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalArtifacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FailedLogins = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TermsVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PrivacyVersion = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PortalArtifactChannels",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CurrentArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MaximumPublishedVersionCode = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalArtifactChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortalArtifactChannels_PortalArtifacts_CurrentArtifactId",
                        column: x => x.CurrentArtifactId,
                        principalTable: "PortalArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PortalDownloadEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RangeRequested = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalDownloadEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortalDownloadEvents_PortalArtifacts_ArtifactId",
                        column: x => x.ArtifactId,
                        principalTable: "PortalArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponsibleName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Province = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OperationType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EstimatedStores = table.Column<int>(type: "int", nullable: true),
                    EstimatedPilots = table.Column<int>(type: "int", nullable: true),
                    EstimatedOrdersPerMonth = table.Column<int>(type: "int", nullable: true),
                    Rnc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BillingAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Profiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProfileSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PublicMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Id);
                    table.UniqueConstraint("AK_Requests_UserId_Id", x => new { x.UserId, x.Id });
                    table.ForeignKey(
                        name: "FK_Requests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stamp = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    RevokedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    UsedAt = table.Column<long>(type: "bigint", nullable: true),
                    Stamp = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccessGrants",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessGrants", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_AccessGrants_Requests_UserId_RequestId",
                        columns: x => new { x.UserId, x.RequestId },
                        principalTable: "Requests",
                        principalColumns: new[] { "UserId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessGrants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Approvals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Approvals_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedAt = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReports", x => x.Id);
                    table.UniqueConstraint("AK_PaymentReports_RequestId_Id", x => new { x.RequestId, x.Id });
                    table.ForeignKey(
                        name: "FK_PaymentReports_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferenceKey = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    VoidedAt = table.Column<long>(type: "bigint", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_PaymentReports_RequestId_ReportId",
                        columns: x => new { x.RequestId, x.ReportId },
                        principalTable: "PaymentReports",
                        principalColumns: new[] { "RequestId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrants_UserId_RequestId",
                table: "AccessGrants",
                columns: new[] { "UserId", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_RequestId",
                table: "Approvals",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Operations_Scope_Key",
                table: "Operations",
                columns: new[] { "Scope", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_SentAt_NextAttemptAt",
                table: "Outbox",
                columns: new[] { "SentAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ReferenceKey",
                table: "Payments",
                column: "ReferenceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_RequestId_ReportId",
                table: "Payments",
                columns: new[] { "RequestId", "ReportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PortalArtifactChannels_CurrentArtifactId",
                table: "PortalArtifactChannels",
                column: "CurrentArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_PortalArtifacts_Sha256",
                table: "PortalArtifacts",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortalDownloadEvents_ArtifactId",
                table: "PortalDownloadEvents",
                column: "ArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_PortalDownloadEvents_UserId_At",
                table: "PortalDownloadEvents",
                columns: new[] { "UserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_UserId",
                table: "Requests",
                column: "UserId",
                unique: true,
                filter: "[Status] IN ('Submitted','InReview','NeedsInformation')");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_UserId_ExpiresAt",
                table: "Sessions",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_UserId_Purpose_Hash",
                table: "Tokens",
                columns: new[] { "UserId", "Purpose", "Hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessGrants");

            migrationBuilder.DropTable(
                name: "Approvals");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "CatalogEntries");

            migrationBuilder.DropTable(
                name: "Operations");

            migrationBuilder.DropTable(
                name: "Outbox");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "PortalArtifactChannels");

            migrationBuilder.DropTable(
                name: "PortalArtifactOperations");

            migrationBuilder.DropTable(
                name: "PortalDownloadEvents");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "Tokens");

            migrationBuilder.DropTable(
                name: "PaymentReports");

            migrationBuilder.DropTable(
                name: "PortalArtifacts");

            migrationBuilder.DropTable(
                name: "Requests");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
