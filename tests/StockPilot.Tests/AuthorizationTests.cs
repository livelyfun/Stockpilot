using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StockPilot.Web.Constants;
using StockPilot.Web.Services;

namespace StockPilot.Tests;

[Collection("postgres")]
public class AuthorizationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];
    private string _staffEmail = string.Empty;
    private string _staffPassword = string.Empty;
    private string _viewerEmail = string.Empty;
    private string _viewerPassword = string.Empty;

    public AuthorizationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _staffEmail = $"staff-{_tag}@stockpilot.test";
        _staffPassword = "Aa1!" + Guid.NewGuid().ToString("N");
        _viewerEmail = $"viewer-{_tag}@stockpilot.test";
        _viewerPassword = "Aa1!" + Guid.NewGuid().ToString("N");

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        var adminId = await _fixture.GetAdminUserIdAsync();

        await users.CreateAsync(new CreateUserInput("Test Staff", _staffEmail, _staffPassword, AppRoles.InventoryStaff), adminId);
        await users.CreateAsync(new CreateUserInput("Test Viewer", _viewerEmail, _viewerPassword, AppRoles.Viewer), adminId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Anonymous_requests_are_redirected_to_login()
    {
        using var client = _fixture.CreateClient();
        var response = await client.GetAsync("/Products");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Administrator_can_reach_administrative_pages()
    {
        using var client = _fixture.CreateClient();
        await PostgresFixture.LoginAsync(client, _fixture.AdminEmail, _fixture.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/AuditLogs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Stock/Adjust")).StatusCode);
    }

    [Fact]
    public async Task Inventory_staff_can_trade_but_cannot_administer_users_or_adjust_stock()
    {
        using var client = _fixture.CreateClient();
        await PostgresFixture.LoginAsync(client, _staffEmail, _staffPassword);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Purchases/Create")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Sales/Create")).StatusCode);

        var users = await client.GetAsync("/Users");
        Assert.Equal(HttpStatusCode.Redirect, users.StatusCode);
        Assert.Contains("/Account/AccessDenied", users.Headers.Location!.ToString());

        var adjust = await client.GetAsync("/Stock/Adjust");
        Assert.Equal(HttpStatusCode.Redirect, adjust.StatusCode);
        Assert.Contains("/Account/AccessDenied", adjust.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Viewer_is_read_only()
    {
        using var client = _fixture.CreateClient();
        await PostgresFixture.LoginAsync(client, _viewerEmail, _viewerPassword);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Products")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Reports")).StatusCode);

        var purchase = await client.GetAsync("/Purchases/Create");
        Assert.Equal(HttpStatusCode.Redirect, purchase.StatusCode);
        Assert.Contains("/Account/AccessDenied", purchase.Headers.Location!.ToString());

        var sale = await client.GetAsync("/Sales/Create");
        Assert.Equal(HttpStatusCode.Redirect, sale.StatusCode);
        Assert.Contains("/Account/AccessDenied", sale.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Unsafe_posts_require_an_antiforgery_token()
    {
        using var client = _fixture.CreateClient();
        await PostgresFixture.LoginAsync(client, _fixture.AdminEmail, _fixture.AdminPassword);

        var withoutToken = await client.PostAsync("/Purchases/Receive/999999", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);

        var page = await client.GetStringAsync("/Purchases");
        var token = PostgresFixture.ExtractAntiforgeryToken(page);
        var withToken = await client.PostAsync("/Purchases/Receive/999999", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        }));

        // A valid token reaches the action; the deliberately missing document yields 404.
        Assert.Equal(HttpStatusCode.NotFound, withToken.StatusCode);
    }
}
