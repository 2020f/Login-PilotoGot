using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Envirapido.Portal.Tests;

public sealed class CoreRequestBodyLimitTests
{
    [Theory]
    [InlineData("application/vnd.envirapido.purchase+json; charset=utf-8")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("Application/Problem+JSON")]
    public async Task Oversized_JSON_and_vendor_JSON_are_rejected_before_model_binding_without_database_writes(string mediaType)
    {
        using var app = new PortalFactory(); await app.InitializeAsync(); using var buyer = app.Buyer();
        int auditBefore = 0; await app.InScopeAsync(async sp => auditBefore = await sp.GetRequiredService<PortalDbContext>().AuditEvents.CountAsync());
        using var body = new StringContent("{\"email\":\"oversized@example.test\",\"padding\":\"" + new string('x', 70000) + "\"}", Encoding.UTF8);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        using var response = await buyer.PostAsync("/portal-api/v1/auth/register", body);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("REQUEST_TOO_LARGE", (await response.Content.ReadFromJsonAsync<PortalError>())!.Code);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<PortalDbContext>(); Assert.Empty(await db.Users.ToListAsync()); Assert.Empty(await db.Requests.ToListAsync());
            Assert.Empty(await db.Outbox.ToListAsync()); Assert.Empty(await db.Payments.ToListAsync()); Assert.Equal(auditBefore, await db.AuditEvents.CountAsync());
        });
    }
}
