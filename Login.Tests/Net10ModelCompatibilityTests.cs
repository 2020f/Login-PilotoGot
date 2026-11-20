using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Login.Application.Services;
using Login.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class Net10ModelCompatibilityTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    [SqlServerFact]
    public Task Sql_model_matches_final_snapshot_without_suppressing_pending_model_changes()
        => app.InScopeAsync(sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.False(db.Database.HasPendingModelChanges(), "La actualización del runtime no puede esconder diferencias del esquema.");
            return Task.CompletedTask;
        });

    [SqlServerFact]
    public Task Bulk_subscription_status_accepts_more_than_sql_server_parameter_limit()
        => app.InScopeAsync(async sp =>
        {
            var ids = Enumerable.Range(1000, 3000).Append(app.TenantA).Append(app.TenantB).ToArray();
            var states = await sp.GetRequiredService<SaasBillingService>().GetExpiredStatesAsync(ids);
            Assert.Equal(2, states.Count); Assert.False(states[app.TenantA]); Assert.False(states[app.TenantB]);
        });

    [Fact]
    public Task Identity_schema_stays_v1_without_passkeys_or_shortened_existing_token_keys()
        => app.InScopeAsync(sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.DoesNotContain(db.Model.GetEntityTypes(), x => x.Name.Contains("IdentityUserPasskey", StringComparison.Ordinal));
            var token = db.Model.FindEntityType(typeof(IdentityUserToken<string>))!;
            Assert.Null(token.FindProperty(nameof(IdentityUserToken<string>.LoginProvider))!.GetMaxLength());
            Assert.Null(token.FindProperty(nameof(IdentityUserToken<string>.Name))!.GetMaxLength());
            var user = db.Model.FindEntityType(typeof(IdentityUser))!;
            Assert.Null(user.FindProperty(nameof(IdentityUser.PhoneNumber))!.GetMaxLength());
            if (db.Database.IsSqlServer())
            {
                Assert.Equal("nvarchar(450)", token.FindProperty(nameof(IdentityUserToken<string>.Name))!.GetColumnType());
                Assert.Equal("nvarchar(max)", user.FindProperty(nameof(IdentityUser.PhoneNumber))!.GetColumnType());
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task Net8_identity_v3_hash_verifies_without_rehash_or_changing_existing_security_stamp()
        => app.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var user = (await users.FindByIdAsync("pilot-a"))!;
            var stamp = user.SecurityStamp;
            // Formato Identity V3 utilizado por .NET 8: PRF=HMACSHA512,
            // 100000 iteraciones, salt de 128 bits y subclave de 256 bits.
            var salt = Enumerable.Range(1, 16).Select(x => (byte)x).ToArray();
            var subkey = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(PilotGoFactory.Password), salt,
                100000, HashAlgorithmName.SHA512, 32);
            var payload = new byte[13 + salt.Length + subkey.Length]; payload[0] = 1;
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(1, 4), 2);
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(5, 4), 100000);
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(9, 4), 16);
            salt.CopyTo(payload, 13); subkey.CopyTo(payload, 29);
            user.PasswordHash = Convert.ToBase64String(payload); await db.SaveChangesAsync();
            var oldHash = user.PasswordHash;
            var hasher = sp.GetRequiredService<IPasswordHasher<IdentityUser>>();
            Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, oldHash!, PilotGoFactory.Password));
            Assert.True(await users.CheckPasswordAsync(user, PilotGoFactory.Password));
            await db.Entry(user).ReloadAsync();
            Assert.Equal(oldHash, user.PasswordHash); Assert.Equal(stamp, user.SecurityStamp);
        });

    [Fact]
    public async Task Existing_mobile_session_json_and_hs256_jwt_survive_and_refresh_with_same_roles_and_stamp()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var refresh = $"{WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("pilot-a"))}.{sessionId}.{WebEncoders.Base64UrlEncode(Enumerable.Repeat((byte)7, 32).ToArray())}";
        var jwt = ""; var stamp = "";
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var user = (await db.Users.FindAsync("pilot-a"))!; stamp = user.SecurityStamp!;
            db.UserTokens.Add(new IdentityUserToken<string> { UserId = user.Id, LoginProvider = "PilotGoMobile", Name = sessionId,
                Value = JsonSerializer.Serialize(new { Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refresh))),
                    ExpiresAt = app.Clock.GetUtcNow().UtcDateTime.AddDays(7), Stamp = stamp }) });
            await db.SaveChangesAsync();
            // Construcción directa del contrato HS256 previo a la actualización;
            // no usa el emisor nuevo para preparar el token que vamos a validar.
            var header = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
            var body = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["sub"] = user.Id, ["email"] = user.Email!, ["unique_name"] = user.UserName!, ["jti"] = Guid.NewGuid().ToString(),
                ["session_id"] = sessionId, ["security_stamp"] = stamp, ["role"] = "Piloto", ["iss"] = "PilotGo", ["aud"] = "PilotGoMobile",
                ["nbf"] = app.Clock.GetUtcNow().AddMinutes(-5).ToUnixTimeSeconds(), ["exp"] = app.Clock.GetUtcNow().AddMinutes(30).ToUnixTimeSeconds()
            }));
            var signingInput = $"{header}.{body}";
            jwt = $"{signingInput}.{WebEncoders.Base64UrlEncode(HMACSHA256.HashData(Encoding.UTF8.GetBytes(PilotGoFactory.JwtKey), Encoding.UTF8.GetBytes(signingInput)))}";
        });
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        await app.InScopeAsync(async sp =>
        {
            var renewed = await sp.GetRequiredService<MobileSessionService>().RefreshAsync(refresh);
            Assert.NotNull(renewed); Assert.Equal(new[] { "Piloto" }, renewed.Roles); Assert.NotEqual(refresh, renewed.RefreshToken);
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(stamp, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == "pilot-a")).SecurityStamp);
            Assert.Single(await db.UserTokens.Where(t => t.UserId == "pilot-a" && t.LoginProvider == "PilotGoMobile").ToListAsync());
        });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}
