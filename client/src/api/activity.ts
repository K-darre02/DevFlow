import { apiFetch } from './client'
import type { Activity, GetActivitiesParams, PagedResponse } from './types'

export function getActivities(params: GetActivitiesParams = {}) {
  const query = new URLSearchParams()
  if (params.page) query.set('page', String(params.page))
  if (params.pageSize) query.set('pageSize', String(params.pageSize))
  if (params.entityType) query.set('entityType', params.entityType)
  if (params.userId) query.set('userId', params.userId)
  if (params.activityType) query.set('activityType', params.activityType)
  const queryString = query.toString()
  return apiFetch<PagedResponse<Activity>>(`/activity${queryString ? `?${queryString}` : ''}`)
}
