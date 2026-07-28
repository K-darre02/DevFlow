using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevFlow.Api.Contracts.Auth;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Xunit;

namespace DevFlow.IntegrationTests.Api;

// Every other test class in this suite mints tokens directly via
// JwtTokenService/DevFlowWebApplicationFactory's helpers, bypassing
// AuthController's own HTTP endpoints entirely — meaning /api/auth/register
// and /api/auth/login had no coverage of their own before this file, despite
// being the one genuinely public (unauthenticated) part of the API surface.
public class AuthControllerTests : IDisposable
{
    // AuthResponse.Role serializes as a string (AddApiServices'
    // JsonStringEnumConverter) — without a matching converter here,
    // ReadFromJsonAsync<AuthResponse> throws deserializing it back.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly DevFlowWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    // Unlike every other test class, nothing here pre-seeds a tenant via
    // CreateDbContextAsync for its own sake — Register is what's under
    // test, it creates the tenant itself. But CreateDbContextAsync is also
    // the only thing that calls Database.EnsureCreatedAsync, so without
    // calling it first, the Sqlite schema doesn't exist yet and the very
    // first query (the email-uniqueness check in AuthController.Register)
    // fails with "no such table: Users" instead of a real assertion result.
    private async Task<HttpClient> CreateClientAsync()
    {
        await _factory.CreateDbContextAsync();
        return _factory.CreateClient();
    }

    [Fact]
    public async Task Register_creates_a_tenant_and_owner_and_returns_a_usable_token()
    {
        var client = await CreateClientAsync();

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            TenantName = "Acme",
            Email = "owner@example.com",
            Password = "correct horse battery staple"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        auth!.AccessToken.Should().NotBeNullOrWhiteSpace();
        auth.Email.Should().Be("owner@example.com");
    }

    [Fact]
    public async Task Register_with_an_already_used_email_returns_409()
    {
        var client = await CreateClientAsync();
        var request = new RegisterRequest { TenantName = "Acme", Email = "dup@example.com", Password = "correct horse battery staple" };

        await client.PostAsJsonAsync("/api/auth/register", request);
        var second = await client.PostAsJsonAsync("/api/auth/register", request);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_with_correct_credentials_returns_a_token()
    {
        var client = await CreateClientAsync();
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            TenantName = "Acme",
            Email = "login-ok@example.com",
            Password = "correct horse battery staple"
        });

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "login-ok@example.com",
            Password = "correct horse battery staple"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        auth!.Email.Should().Be("login-ok@example.com");
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var client = await CreateClientAsync();
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            TenantName = "Acme",
            Email = "login-bad@example.com",
            Password = "correct horse battery staple"
        });

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "login-bad@example.com",
            Password = "wrong password entirely"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_for_an_unknown_email_returns_401_not_404()
    {
        var client = await CreateClientAsync();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "nobody@example.com",
            Password = "whatever it might be"
        });

        // 401, not 404 — doesn't confirm or deny whether the email is
        // registered (same "don't leak existence" reasoning as tenant
        // isolation's 404-not-403 convention elsewhere in this suite).
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_is_rate_limited_after_repeated_failures_from_the_same_client()
    {
        var client = await CreateClientAsync();
        var request = new LoginRequest { Email = "nobody@example.com", Password = "wrong" };

        // AuthRateLimitPolicy permits 10 requests/minute per client IP
        // (DevFlow.Api.DependencyInjection) — the 11th in the same window
        // must be rejected rather than quietly falling through the limiter.
        HttpResponseMessage? last = null;
        for (var i = 0; i < 11; i++)
        {
            last = await client.PostAsJsonAsync("/api/auth/login", request);
        }

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
