import type { QueryClient } from '@tanstack/react-query'
import type { HubConnection } from '@microsoft/signalr'
import type { Notification, UnreadCountResponse } from '../api/types'

export function registerNotificationEventHandlers(connection: HubConnection, queryClient: QueryClient): void {
  connection.on('notification.created', (_notification: Notification) => {
    // Unlike task/project lists, the notification list is paginated rather
    // than one always-fully-cached array — invalidating (instead of
    // patching a specific page in place) is simpler and correct regardless
    // of which page, if any, is currently mounted; whichever notification
    // queries are actively being viewed just refetch.
    void queryClient.invalidateQueries({ queryKey: ['notifications'] })

    queryClient.setQueryData<UnreadCountResponse>(['notifications', 'unread-count'], (old) => ({
      count: (old?.count ?? 0) + 1,
    }))
  })
}
