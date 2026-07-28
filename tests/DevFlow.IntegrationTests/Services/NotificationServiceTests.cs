using DevFlow.Application.Notifications;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Xunit;

namespace DevFlow.IntegrationTests.Services;

public class NotificationServiceTests : SqliteContextFixture
{
    private readonly INotificationService _service;
    private readonly Tenant _tenant;
    private readonly User _user;

    public NotificationServiceTests()
    {
        _service = new NotificationService(DbContext);

        _tenant = new Tenant { Name = "Tenant" };
        _user = new User { Email = "user@example.com", PasswordHash = "unused" };
        DbContext.AddRange(_tenant, _user);
        DbContext.SaveChangesAsync().GetAwaiter().GetResult();

        CurrentUser.TenantId = _tenant.Id;
        CurrentUser.UserId = _user.Id;
    }

    private async Task<Notification> SeedNotificationAsync(Guid? userId = null, bool isRead = false, string title = "Title")
    {
        var notification = new Notification
        {
            TenantId = _tenant.Id,
            UserId = userId ?? _user.Id,
            Type = NotificationType.TaskAssigned,
            Title = title,
            Message = "Message",
            IsRead = isRead
        };
        DbContext.Notifications.Add(notification);
        await DbContext.SaveChangesAsync();
        return notification;
    }

    [Fact]
    public async Task GetNotificationsAsync_returns_results_newest_first()
    {
        var first = await SeedNotificationAsync(title: "First");
        var second = await SeedNotificationAsync(title: "Second");
        var third = await SeedNotificationAsync(title: "Third");

        var result = await _service.GetNotificationsAsync(new NotificationQuery(1, 20), default);

        result.Items.Select(n => n.Id).Should().ContainInOrder(third.Id, second.Id, first.Id);
        result.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetNotificationsAsync_paginates_correctly()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedNotificationAsync(title: $"Notification {i}");
        }

        var pageOne = await _service.GetNotificationsAsync(new NotificationQuery(1, 2), default);
        var pageTwo = await _service.GetNotificationsAsync(new NotificationQuery(2, 2), default);

        pageOne.Items.Should().HaveCount(2);
        pageTwo.Items.Should().HaveCount(2);
        pageOne.TotalCount.Should().Be(5);
        pageOne.Items.Select(n => n.Id).Should().NotIntersectWith(pageTwo.Items.Select(n => n.Id));
    }

    [Fact]
    public async Task GetNotificationsAsync_only_returns_the_current_users_notifications()
    {
        var otherUser = new User { Email = "other@example.com", PasswordHash = "unused" };
        DbContext.Users.Add(otherUser);
        await DbContext.SaveChangesAsync();

        await SeedNotificationAsync(userId: _user.Id, title: "Mine");
        await SeedNotificationAsync(userId: otherUser.Id, title: "Not mine");

        var result = await _service.GetNotificationsAsync(new NotificationQuery(1, 20), default);

        result.Items.Should().ContainSingle().Which.Title.Should().Be("Mine");
    }

    [Fact]
    public async Task GetNotificationsAsync_only_returns_the_current_tenants_notifications()
    {
        var otherTenant = new Tenant { Name = "Other Tenant" };
        DbContext.Tenants.Add(otherTenant);
        await DbContext.SaveChangesAsync();

        await SeedNotificationAsync(title: "This tenant");

        // Same UserId, different tenant — proves the filter checks TenantId
        // *and* UserId together, not either alone.
        var otherTenantNotification = new Notification
        {
            TenantId = otherTenant.Id,
            UserId = _user.Id,
            Type = NotificationType.TaskAssigned,
            Title = "Other tenant",
            Message = "Message"
        };
        DbContext.Notifications.Add(otherTenantNotification);
        await DbContext.SaveChangesAsync();

        var result = await _service.GetNotificationsAsync(new NotificationQuery(1, 20), default);

        result.Items.Should().ContainSingle().Which.Title.Should().Be("This tenant");
    }

    [Fact]
    public async Task GetUnreadCountAsync_counts_only_unread_notifications_for_the_current_user()
    {
        await SeedNotificationAsync(isRead: false);
        await SeedNotificationAsync(isRead: false);
        await SeedNotificationAsync(isRead: true);

        var count = await _service.GetUnreadCountAsync(default);

        count.Should().Be(2);
    }

    [Fact]
    public async Task MarkAsReadAsync_marks_the_notification_read()
    {
        var notification = await SeedNotificationAsync(isRead: false);

        var updated = await _service.MarkAsReadAsync(notification.Id, default);

        updated.Should().NotBeNull();
        updated!.IsRead.Should().BeTrue();
        (await _service.GetUnreadCountAsync(default)).Should().Be(0);
    }

    [Fact]
    public async Task MarkAsReadAsync_returns_null_for_a_notification_that_does_not_exist()
    {
        var result = await _service.MarkAsReadAsync(Guid.NewGuid(), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task MarkAsReadAsync_returns_null_for_another_users_notification()
    {
        var otherUser = new User { Email = "other@example.com", PasswordHash = "unused" };
        DbContext.Users.Add(otherUser);
        await DbContext.SaveChangesAsync();
        var otherUsersNotification = await SeedNotificationAsync(userId: otherUser.Id);

        var result = await _service.MarkAsReadAsync(otherUsersNotification.Id, default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task MarkAllAsReadAsync_marks_every_unread_notification_read_and_returns_the_count()
    {
        await SeedNotificationAsync(isRead: false);
        await SeedNotificationAsync(isRead: false);
        await SeedNotificationAsync(isRead: true);

        var markedCount = await _service.MarkAllAsReadAsync(default);

        markedCount.Should().Be(2);
        (await _service.GetUnreadCountAsync(default)).Should().Be(0);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_does_not_affect_another_users_notifications()
    {
        var otherUser = new User { Email = "other@example.com", PasswordHash = "unused" };
        DbContext.Users.Add(otherUser);
        await DbContext.SaveChangesAsync();
        await SeedNotificationAsync(userId: otherUser.Id, isRead: false);
        await SeedNotificationAsync(userId: _user.Id, isRead: false);

        await _service.MarkAllAsReadAsync(default);

        CurrentUser.UserId = otherUser.Id;
        (await _service.GetUnreadCountAsync(default)).Should().Be(1);
    }
}
