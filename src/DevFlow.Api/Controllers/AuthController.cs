using DevFlow.Api.Contracts.Auth;
using DevFlow.Api.Services;
using DevFlow.Domain.Entities;
using DevFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly DevFlowDbContext _dbContext;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly JwtTokenService _tokenService;

    public AuthController(DevFlowDbContext dbContext, IPasswordHasher<User> passwordHasher, JwtTokenService tokenService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    // Creates a new Tenant and its first User atomically. There is no
    // authenticated tenant context yet at this point in the request, so the
    // email-uniqueness check below deliberately bypasses the tenant query
    // filter (IgnoreQueryFilters) — see DevFlowDbContext for why that filter
    // exists and why this is the documented, legitimate way to opt out of it.
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var emailTaken = await _dbContext.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == request.Email, cancellationToken);

        if (emailTaken)
        {
            return Conflict("A user with this email already exists.");
        }

        var tenant = new Tenant { Name = request.TenantName };

        var user = new User
        {
            TenantId = tenant.Id,
            Tenant = tenant,
            Email = request.Email
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _dbContext.Tenants.Add(tenant);
        _dbContext.Users.Add(user);

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

        var (accessToken, expiresAt) = _tokenService.GenerateToken(user);

        // Plain 201 rather than CreatedAtAction: there's no GetUser/GetTenant
        // endpoint yet for a Location header to meaningfully point at.
        return StatusCode(
            StatusCodes.Status201Created,
            new AuthResponse(accessToken, expiresAt, tenant.Id, user.Id, user.Email));
    }

    // Same IgnoreQueryFilters reasoning as Register: no tenant is known yet —
    // finding out which tenant this user belongs to is the whole point of this call.
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        if (user is null)
        {
            return Unauthorized("Invalid email or password.");
        }

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            return Unauthorized("Invalid email or password.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var (accessToken, expiresAt) = _tokenService.GenerateToken(user);

        return Ok(new AuthResponse(accessToken, expiresAt, user.TenantId, user.Id, user.Email));
    }
}
