using DevFlow.Api.Contracts.Auth;
using DevFlow.Api.Services;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting(DependencyInjection.AuthRateLimitPolicy)]
public class AuthController : ControllerBase
{
    private readonly DevFlowDbContext _dbContext;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly JwtTokenService _tokenService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        DevFlowDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        JwtTokenService tokenService,
        ILogger<AuthController> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _logger = logger;
    }

    // Creates a new Tenant, its first User, and a TenantMember(Owner) linking
    // them, atomically. Users has no tenant query filter at all (it's a
    // global entity — see User.cs), so no IgnoreQueryFilters is needed here
    // the way it is for TenantMembers/Invitations elsewhere.
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var emailTaken = await _dbContext.Users.AnyAsync(u => u.Email == request.Email, cancellationToken);

        if (emailTaken)
        {
            return Conflict("A user with this email already exists.");
        }

        var tenant = new Tenant { Name = request.TenantName };

        var user = new User { Email = request.Email };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        var membership = new TenantMember
        {
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            Role = TenantRole.Owner
        };

        _dbContext.Tenants.Add(tenant);
        _dbContext.Users.Add(user);
        _dbContext.TenantMembers.Add(membership);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Backstop for the race between the AnyAsync check above and this
            // insert — the unique index on Email (UserConfiguration) is the
            // actual enforcement; this just translates its violation to a 409
            // instead of a raw 500.
            return Conflict("A user with this email already exists.");
        }

        var (accessToken, expiresAt) = _tokenService.GenerateToken(user, tenant.Id, TenantRole.Owner);

        // Plain 201 rather than CreatedAtAction: there's no GetUser/GetTenant
        // endpoint yet for a Location header to meaningfully point at.
        return StatusCode(
            StatusCodes.Status201Created,
            new AuthResponse(accessToken, expiresAt, tenant.Id, user.Id, user.Email, TenantRole.Owner));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        if (user is null)
        {
            _logger.LogWarning("Login failed for {Email}: no account with this email.", request.Email);
            return Unauthorized("Invalid email or password.");
        }

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Login failed for {Email}: incorrect password.", request.Email);
            return Unauthorized("Invalid email or password.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // A User can hold memberships in multiple tenants (TenantMember).
        // There's no tenant-picker UI yet, so a login token is issued for
        // the earliest (first-joined) membership — a deliberate, documented
        // simplification, not an oversight; a real multi-tenant switcher is
        // a separate feature. IgnoreQueryFilters: no tenant context exists
        // yet, discovering one is the point of this query.
        // Orders by CreatedAtTicks, not CreatedAt directly — see
        // TenantMember.CreatedAtTicks (SQLite can't translate ORDER BY on a
        // DateTimeOffset column; same fix already applied to
        // ActivityLog/Notification).
        var membership = await _dbContext.TenantMembers
            .IgnoreQueryFilters()
            .Where(m => m.UserId == user.Id)
            .OrderBy(m => m.CreatedAtTicks)
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            // Not reachable via this system's own flows today (Register and
            // Invitation-Accept both always create a membership) — defended
            // against anyway rather than risking a NullReferenceException.
            return Unauthorized("This account does not belong to any workspace.");
        }

        var (accessToken, expiresAt) = _tokenService.GenerateToken(user, membership.TenantId, membership.Role);

        return Ok(new AuthResponse(accessToken, expiresAt, membership.TenantId, user.Id, user.Email, membership.Role));
    }
}
