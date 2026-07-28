import { useState } from 'react'
import { DndContext, KeyboardSensor, PointerSensor, useSensor, useSensors, type DragEndEvent } from '@dnd-kit/core'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../api/client'
import { getTasks, updateTask } from '../../api/tasks'
import type { Task, TaskItemStatus } from '../../api/types'
import { STATUS_COLUMNS } from '../../lib/taskDisplay'
import { TaskFormModal } from '../tasks/TaskFormModal'
import { Button } from '../ui/Button'
import { ErrorBanner } from '../ui/ErrorBanner'
import { Spinner } from '../ui/Spinner'
import { KanbanColumn } from './KanbanColumn'

interface KanbanBoardProps {
  projectId: string
}

interface MutationContext {
  previous?: Task[]
}

export function KanbanBoard({ projectId }: KanbanBoardProps) {
  const queryClient = useQueryClient()
  const tasksQueryKey = ['tasks', projectId]
  const tasksQuery = useQuery({ queryKey: tasksQueryKey, queryFn: () => getTasks({ projectId }) })

  const [editingTask, setEditingTask] = useState<Task | null>(null)
  const [isCreating, setIsCreating] = useState(false)
  const [conflictNotice, setConflictNotice] = useState<string | null>(null)

  // A small activation distance lets a plain click on a card still fire as a
  // click (opening the edit modal) rather than every click being captured
  // as a zero-distance drag. KeyboardSensor gives the board a non-pointer
  // way to move a focused card between columns — see
  // docs/devflow/07-quality-attributes.md §6 (accessibility target).
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor),
  )

  // Optimistic status update: the card moves immediately in the UI, then
  // reconciles with the server. On a genuine conflict (409 — someone else
  // changed the task first), the response's `current` field is the
  // authoritative state; that's what gets written into the cache, not a
  // blind revert, so the board reflects reality rather than either the
  // failed optimistic guess or stale pre-drag data. See
  // docs/devflow/06-engineering-challenges.md §2.
  const statusMutation = useMutation<Task, unknown, { task: Task; status: TaskItemStatus }, MutationContext>({
    mutationFn: ({ task, status }) => updateTask(task.id, { status }, task.version),
    onMutate: async ({ task, status }) => {
      await queryClient.cancelQueries({ queryKey: tasksQueryKey })
      const previous = queryClient.getQueryData<Task[]>(tasksQueryKey)
      queryClient.setQueryData<Task[]>(tasksQueryKey, (old) =>
        old?.map((item) => (item.id === task.id ? { ...item, status } : item)),
      )
      return { previous }
    },
    onError: (error, _vars, context) => {
      if (context?.previous) {
        queryClient.setQueryData(tasksQueryKey, context.previous)
      }
      if (error instanceof ApiError && error.status === 409 && error.conflict) {
        const current = error.conflict as Task
        queryClient.setQueryData<Task[]>(tasksQueryKey, (old) =>
          old?.map((item) => (item.id === current.id ? current : item)),
        )
        setConflictNotice(`"${current.title}" was changed elsewhere — showing the latest version.`)
      } else {
        setConflictNotice('Could not update the task. Please try again.')
      }
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: tasksQueryKey })
    },
  })

  function handleDragEnd(event: DragEndEvent) {
    const { active, over } = event
    if (!over) {
      return
    }

    const task = tasksQuery.data?.find((item) => item.id === active.id)
    const newStatus = over.id as TaskItemStatus
    if (!task || task.status === newStatus) {
      return
    }

    statusMutation.mutate({ task, status: newStatus })
  }

  function refetchTasks() {
    void queryClient.invalidateQueries({ queryKey: tasksQueryKey })
  }

  if (tasksQuery.isLoading) {
    return <Spinner label="Loading tasks…" />
  }

  if (tasksQuery.isError) {
    return (
      <ErrorBanner
        messages={tasksQuery.error instanceof ApiError ? tasksQuery.error.messages : ['Could not load tasks.']}
      />
    )
  }

  const tasks = tasksQuery.data ?? []

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold text-slate-900">Board</h2>
        <Button onClick={() => setIsCreating(true)}>+ New task</Button>
      </div>

      {conflictNotice && (
        <div className="flex items-start justify-between gap-3 rounded-md border border-amber-200 bg-amber-50 px-4 py-2 text-sm text-amber-800">
          <span>{conflictNotice}</span>
          <button type="button" onClick={() => setConflictNotice(null)} className="text-amber-600 hover:text-amber-900">
            Dismiss
          </button>
        </div>
      )}

      <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-5">
          {STATUS_COLUMNS.map(({ status, label }) => (
            <KanbanColumn
              key={status}
              status={status}
              label={label}
              tasks={tasks.filter((task) => task.status === status)}
              onTaskClick={setEditingTask}
            />
          ))}
        </div>
      </DndContext>

      <TaskFormModal
        isOpen={isCreating}
        onClose={() => setIsCreating(false)}
        projectId={projectId}
        onSaved={refetchTasks}
      />

      <TaskFormModal
        isOpen={editingTask !== null}
        onClose={() => setEditingTask(null)}
        projectId={projectId}
        task={editingTask ?? undefined}
        onSaved={refetchTasks}
      />
    </div>
  )
}
