import type { TaskItemStatus, TaskPriority } from '../api/types'

// Fixed, ordered column set — matches the enum's defined order (not
// user-configurable; see docs/devflow/05-technical-decisions.md ADR 7).
export const STATUS_COLUMNS: { status: TaskItemStatus; label: string }[] = [
  { status: 'Backlog', label: 'Backlog' },
  { status: 'ToDo', label: 'To Do' },
  { status: 'InProgress', label: 'In Progress' },
  { status: 'InReview', label: 'In Review' },
  { status: 'Done', label: 'Done' },
]

export const PRIORITY_OPTIONS: TaskPriority[] = ['Low', 'Medium', 'High', 'Urgent']

export const PRIORITY_BADGE_COLOR: Record<TaskPriority, 'gray' | 'blue' | 'orange' | 'red'> = {
  Low: 'gray',
  Medium: 'blue',
  High: 'orange',
  Urgent: 'red',
}

// Solid bar-fill classes for DashboardPage's tasks-by-status chart — a
// separate map from PRIORITY_BADGE_COLOR's named colors (Badge resolves
// those itself) since a chart bar needs the raw Tailwind class directly.
export const STATUS_BAR_COLOR: Record<TaskItemStatus, string> = {
  Backlog: 'bg-slate-400',
  ToDo: 'bg-sky-500',
  InProgress: 'bg-amber-500',
  InReview: 'bg-purple-500',
  Done: 'bg-green-500',
}

export const PRIORITY_BAR_COLOR: Record<TaskPriority, string> = {
  Low: 'bg-slate-400',
  Medium: 'bg-blue-500',
  High: 'bg-orange-500',
  Urgent: 'bg-red-500',
}
