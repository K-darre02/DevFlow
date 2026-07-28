import { apiFetch } from './client'
import type { GetNotificationsParams, Notification, PagedResponse, UnreadCountResponse } from './types'

export function getNotifications(params: GetNotificationsParams = {}) {
  const query = new URLSearchParams()
  if (params.page) query.set('page', String(params.page))
  if (params.pageSize) query.set('pageSize', String(params.pageSize))
  const queryString = query.toString()
  return apiFetch<PagedResponse<Notification>>(`/notifications${queryString ? `?${queryString}` : ''}`)
}

export function getUnreadCount() {
  return apiFetch<UnreadCountResponse>('/notifications/unread-count')
}

export function markNotificationAsRead(id: string) {
  return apiFetch<Notification>(`/notifications/${id}/read`, { method: 'PATCH' })
}

export function markAllNotificationsAsRead() {
  return apiFetch<void>('/notifications/read-all', { method: 'PATCH' })
}
