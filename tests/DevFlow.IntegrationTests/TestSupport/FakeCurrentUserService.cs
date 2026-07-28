using DevFlow.Application.Common;

namespace DevFlow.IntegrationTests.TestSupport;

public sealed class FakeCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; }

    public Guid? TenantId { get; set; }
}
