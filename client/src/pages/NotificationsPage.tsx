import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { getNotifications, markAllNotificationsAsRead, markNotificationAsRead } from '../api/notifications'
import { ApiError } from '../api/client'
import { Button } from '../components/ui/Button'
import { Card } from '../components/ui/Card'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Spinner } from '../components/ui/Spinner'

const PAGE_SIZE = 20

export function NotificationsPage() {
  const [page, setPage] = useState(1)
  const queryClient = useQueryClient()

  const notificationsQuery = useQuery({
    queryKey: ['notifications', 'list', page],
    queryFn: () => getNotifications({ page, pageSize: PAGE_SIZE }),
  })

  const markReadMutation = useMutation({
    mutationFn: markNotificationAsRead,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['notifications'] }),
  })

  const markAllMutation = useMutation({
    mutationFn: markAllNotificationsAsRead,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['notifications'] }),
  })

  const totalPages = notificationsQuery.data ? Math.max(1, Math.ceil(notificationsQuery.data.totalCount / PAGE_SIZE)) : 1
  const hasUnread = notificationsQuery.data?.items.some((notification) => !notification.isRead) ?? false

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-slate-900">Notifications</h2>
          <Button
            type="button"
            variant="secondary"
            disabled={!hasUnread}
            isLoading={markAllMutation.isPending}
            onClick={() => markAllMutation.mutate()}
          >
            Mark all as read
          </Button>
        </div>

        {notificationsQuery.isLoading && <Spinner label="Loading notifications…" />}

        {notificationsQuery.isError && (
          <ErrorBanner
            messages={
              notificationsQuery.error instanceof ApiError ? notificationsQuery.error.messages : ['Could not load notifications.']
            }
          />
        )}

        {notificationsQuery.data?.items.length === 0 && (
          <p className="text-sm text-slate-500">No notifications yet.</p>
        )}

        {notificationsQuery.data && notificationsQuery.data.items.length > 0 && (
          <ul className="flex flex-col divide-y divide-slate-100">
            {notificationsQuery.data.items.map((notification) => (
              <li key={notification.id} className={`flex items-start justify-between gap-4 py-3 ${notification.isRead ? '' : 'bg-blue-50/40'}`}>
                <div>
                  <p className="text-sm font-medium text-slate-900">{notification.title}</p>
                  <p className="mt-0.5 text-sm text-slate-600">{notification.message}</p>
                  <p className="mt-1 text-xs text-slate-400">{new Date(notification.createdAt).toLocaleString()}</p>
                </div>
                {!notification.isRead && (
                  <Button
                    type="button"
                    variant="secondary"
                    isLoading={markReadMutation.isPending && markReadMutation.variables === notification.id}
                    onClick={() => markReadMutation.mutate(notification.id)}
                  >
                    Mark as read
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}

        {notificationsQuery.data && notificationsQuery.data.totalCount > 0 && (
          <div className="mt-6 flex items-center justify-between border-t border-slate-200 pt-4">
            <span className="text-xs text-slate-500">
              Page {notificationsQuery.data.page} of {totalPages} · {notificationsQuery.data.totalCount} total
            </span>
            <div className="flex gap-2">
              <Button
                type="button"
                variant="secondary"
                disabled={page <= 1}
                onClick={() => setPage((current) => Math.max(1, current - 1))}
              >
                Previous
              </Button>
              <Button
                type="button"
                variant="secondary"
                disabled={page >= totalPages}
                onClick={() => setPage((current) => current + 1)}
              >
                Next
              </Button>
            </div>
          </div>
        )}
      </Card>
    </div>
  )
}
