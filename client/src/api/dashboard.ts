import { apiFetch } from './client'
import type { DashboardSummary } from './types'

export function getDashboard() {
  return apiFetch<DashboardSummary>('/dashboard')
}
