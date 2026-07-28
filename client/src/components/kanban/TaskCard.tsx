import { useDraggable } from '@dnd-kit/core'
import { useAuthStore } from '../../auth/authStore'
import type { Task } from '../../api/types'
import { PRIORITY_BADGE_COLOR } from '../../lib/taskDisplay'
import { Badge } from '../ui/Badge'

interface TaskCardProps {
  task: Task
  onClick: () => void
}

function formatDueDate(dueDate: string) {
  const date = new Date(`${dueDate}T00:00:00`)
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  return {
    label: date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
    isOverdue: date < today,
  }
}

export function TaskCard({ task, onClick }: TaskCardProps) {
  const currentUser = useAuthStore((state) => state.user)
  // Small activation distance (set on the sensor in KanbanBoard) lets a
  // plain click still fire normally — only a real drag captures the pointer.
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({ id: task.id })

  const due = task.dueDate ? formatDueDate(task.dueDate) : null
  // Single-user-per-tenant for now (no invite/membership flow exists yet),
  // so the only assignee this UI can ever produce is "me". A task assigned
  // to some other id (only reachable by calling the API directly) still
  // renders sensibly via the "Assigned" fallback below.
  const isAssignedToMe = Boolean(task.assigneeUserId) && task.assigneeUserId === currentUser?.userId

  return (
    <div
      ref={setNodeRef}
      style={transform ? { transform: `translate3d(${transform.x}px, ${transform.y}px, 0)` } : undefined}
      onClick={onClick}
      {...listeners}
      {...attributes}
      className={`cursor-pointer touch-none rounded-md border border-slate-200 bg-white p-3 shadow-sm
        transition-opacity hover:border-slate-300 ${isDragging ? 'opacity-50' : ''}`}
    >
      <p className="text-sm font-medium text-slate-900">{task.title}</p>
      <div className="mt-2 flex flex-wrap items-center gap-1.5">
        <Badge color={PRIORITY_BADGE_COLOR[task.priority]}>{task.priority}</Badge>
        {due && (
          <Badge color={due.isOverdue ? 'red' : 'gray'}>
            {due.isOverdue ? 'Overdue · ' : ''}
            {due.label}
          </Badge>
        )}
        {task.assigneeUserId && <Badge color="green">{isAssignedToMe ? 'Me' : 'Assigned'}</Badge>}
      </div>
    </div>
  )
}
