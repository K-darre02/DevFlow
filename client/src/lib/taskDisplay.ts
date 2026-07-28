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
