import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { getNotifications, getUnreadCount, markAllNotificationsAsRead, markNotificationAsRead } from '../../api/notifications'
import { Spinner } from '../ui/Spinner'

export function NotificationBell() {
  const [isOpen, setIsOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)
  const queryClient = useQueryClient()

  const unreadQuery = useQuery({ queryKey: ['notifications', 'unread-count'], queryFn: getUnreadCount })
  // Only fetched once the dropdown is actually opened — no point loading a
  // notification list nobody has asked to see yet.
  const recentQuery = useQuery({
    queryKey: ['notifications', 'recent'],
    queryFn: () => getNotifications({ pageSize: 5 }),
    enabled: isOpen,
  })

  useEffect(() => {
    if (!isOpen) return

    function handleClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setIsOpen(false)
      }
    }

    document.addEventListener('mousedown', handleClickOutside)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [isOpen])

  const markReadMutation = useMutation({
    mutationFn: markNotificationAsRead,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['notifications'] }),
  })

  const markAllMutation = useMutation({
    mutationFn: markAllNotificationsAsRead,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['notifications'] }),
  })

  const unreadCount = unreadQuery.data?.count ?? 0

  return (
    <div className="relative" ref={containerRef}>
      <button
        type="button"
        onClick={() => setIsOpen((open) => !open)}
        aria-label={`Notifications${unreadCount > 0 ? ` (${unreadCount} unread)` : ''}`}
        aria-expanded={isOpen}
        className="relative rounded-md p-2 text-slate-500 hover:bg-slate-100 hover:text-slate-700"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="h-5 w-5" aria-hidden="true">
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            d="M15 17h5l-1.4-1.4A2 2 0 0 1 18 14.2V11a6 6 0 0 0-4-5.66V5a2 2 0 1 0-4 0v.34A6 6 0 0 0 6 11v3.2a2 2 0 0 1-.6 1.4L4 17h5m6 0v1a3 3 0 1 1-6 0v-1m6 0H9"
          />
        </svg>
        {unreadCount > 0 && (
          <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-red-600 px-1 text-[10px] font-semibold text-white">
            {unreadCount > 9 ? '9+' : unreadCount}
          </span>
        )}
      </button>

      {isOpen && (
        <div className="absolute right-0 z-40 mt-2 w-80 rounded-lg border border-slate-200 bg-white shadow-lg">
          <div className="flex items-center justify-between border-b border-slate-100 px-4 py-2">
            <span className="text-sm font-semibold text-slate-900">Notifications</span>
            {unreadCount > 0 && (
              <button
                type="button"
                onClick={() => markAllMutation.mutate()}
                className="text-xs font-medium text-slate-500 hover:text-slate-700"
              >
                Mark all as read
              </button>
            )}
          </div>

          <div className="max-h-80 overflow-y-auto">
            {recentQuery.isLoading && <Spinner label="Loading…" />}

            {recentQuery.data?.items.length === 0 && (
              <p className="px-4 py-6 text-center text-sm text-slate-500">No notifications yet.</p>
            )}

            {recentQuery.data?.items.map((notification) => (
              <button
                key={notification.id}
                type="button"
                onClick={() => !notification.isRead && markReadMutation.mutate(notification.id)}
                className={`block w-full border-b border-slate-50 px-4 py-3 text-left last:border-0 hover:bg-slate-50 ${
                  notification.isRead ? '' : 'bg-blue-50/60'
                }`}
              >
                <p className="text-sm font-medium text-slate-900">{notification.title}</p>
                <p className="mt-0.5 text-xs text-slate-600">{notification.message}</p>
                <p className="mt-1 text-xs text-slate-400">{new Date(notification.createdAt).toLocaleString()}</p>
              </button>
            ))}
          </div>

          <div className="border-t border-slate-100 px-4 py-2 text-center">
            <Link
              to="/notifications"
              onClick={() => setIsOpen(false)}
              className="text-xs font-medium text-slate-600 hover:text-slate-900"
            >
              View all notifications
            </Link>
          </div>
        </div>
      )}
    </div>
  )
}
