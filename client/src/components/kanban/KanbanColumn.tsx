import { useDroppable } from '@dnd-kit/core'
import type { Task, TaskItemStatus } from '../../api/types'
import { TaskCard } from './TaskCard'

interface KanbanColumnProps {
  status: TaskItemStatus
  label: string
  tasks: Task[]
  onTaskClick: (task: Task) => void
}

export function KanbanColumn({ status, label, tasks, onTaskClick }: KanbanColumnProps) {
  const { setNodeRef, isOver } = useDroppable({ id: status })

  return (
    <div
      ref={setNodeRef}
      className={`flex min-h-[200px] flex-col gap-2 rounded-lg border p-3 transition-colors ${
        isOver ? 'border-slate-400 bg-slate-100' : 'border-slate-200 bg-slate-50'
      }`}
    >
      <div className="flex items-center justify-between px-1">
        <h3 className="text-sm font-semibold text-slate-700">{label}</h3>
        <span className="text-xs text-slate-400">{tasks.length}</span>
      </div>

      {tasks.length === 0 && <p className="px-1 text-xs text-slate-400">No tasks</p>}

      {tasks.map((task) => (
        <TaskCard key={task.id} task={task} onClick={() => onTaskClick(task)} />
      ))}
    </div>
  )
}
