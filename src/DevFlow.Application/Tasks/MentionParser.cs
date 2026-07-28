using System.Text.RegularExpressions;
using DevFlow.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Tasks;

// No dedicated "username" field exists in this system — only email — so a
// mention is written as "@" followed by the local part of a tenant member's
// email (e.g. "@alice" matches alice@example.com), case-insensitive. A
// simplification, not a general-purpose mention syntax, but it demonstrates
// the feature without inventing a whole handle/username subsystem for it.
internal static class MentionParser
{
    private static readonly Regex MentionPattern = new(@"@(\w+)", RegexOptions.Compiled);

    public static async Task<IReadOnlyList<Guid>> ResolveMentionedUserIdsAsync(
        IApplicationDbContext context, string? description, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Array.Empty<Guid>();
        }

        var handles = MentionPattern.Matches(description)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (handles.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        // Tenant-scoped for free via the ambient query filter — a mention
        // can only ever resolve to someone who's actually a member of this
        // tenant, never an arbitrary user elsewhere in the system.
        var members = await context.TenantMembers.Include(m => m.User).ToListAsync(cancellationToken);

        return members
            .Where(member => handles.Any(handle =>
                string.Equals(member.User.Email.Split('@')[0], handle, StringComparison.OrdinalIgnoreCase)))
            .Select(member => member.UserId)
            .Distinct()
            .ToList();
    }
}
