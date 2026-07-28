using DevFlow.Application.Common;
using DevFlow.Domain.Enums;

namespace DevFlow.IntegrationTests.TestSupport;

public sealed class FakeCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; }

    public Guid? TenantId { get; set; }

    public TenantRole? Role { get; set; }
}
