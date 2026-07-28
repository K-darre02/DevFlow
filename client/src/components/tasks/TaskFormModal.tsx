import { useEffect, useState, type FormEvent } from 'react'
import { useMutation } from '@tanstack/react-query'
import { ApiError } from '../../api/client'
import { createTask, updateTask } from '../../api/tasks'
import type { Task, TaskItemStatus, TaskPriority } from '../../api/types'
import { useAuthStore } from '../../auth/authStore'
import { PRIORITY_OPTIONS, STATUS_COLUMNS } from '../../lib/taskDisplay'
import { Button } from '../ui/Button'
import { ErrorBanner } from '../ui/ErrorBanner'
import { Input } from '../ui/Input'
import { Modal } from '../ui/Modal'
import { Select } from '../ui/Select'
import { TaskAttachments } from './TaskAttachments'

interface TaskFormModalProps {
  isOpen: boolean
  onClose: () => void
  projectId: string
  /** Present → edit mode; absent → create mode. */
  task?: Task
  onSaved: () => void
}

export function TaskFormModal({ isOpen, onClose, projectId, task, onSaved }: TaskFormModalProps) {
  const currentUser = useAuthStore((state) => state.user)
  const isEditing = task !== undefined

  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [priority, setPriority] = useState<TaskPriority>('Medium')
  const [status, setStatus] = useState<TaskItemStatus>('Backlog')
  const [dueDate, setDueDate] = useState('')
  const [assignToMe, setAssignToMe] = useState(false)
  const [errors, setErrors] = useState<string[]>([])

  // There's no invite/team-membership flow yet, so the only user this system
  // knows about is whoever is logged in — "assign" can only ever mean
  // "assign to me". The update endpoint also can't explicitly *clear* an
  // existing assignment (PATCH semantics: an omitted field means "no
  // change", not "set to null" — see UpdateTaskInput.cs), so once a task is
  // assigned, this control is replaced with a static label instead of
  // offering an "unassign" action that wouldn't actually do anything.
  const canOfferAssignToMe = !isEditing || !task.assigneeUserId

  useEffect(() => {
    if (!isOpen) return
    setTitle(task?.title ?? '')
    setDescription(task?.description ?? '')
    setPriority(task?.priority ?? 'Medium')
    setStatus(task?.status ?? 'Backlog')
    setDueDate(task?.dueDate ?? '')
    setAssignToMe(false)
    setErrors([])
  }, [isOpen, task])

  const mutation = useMutation({
    mutationFn: () => {
      const assigneeUserId = assignToMe ? currentUser?.userId : undefined

      if (task) {
        return updateTask(
          task.id,
          {
            title,
            description: description || undefined,
            status,
            priority,
            assigneeUserId,
            dueDate: dueDate || undefined,
          },
          task.version,
        )
      }

      return createTask({
        projectId,
        title,
        description: description || undefined,
        priority,
        assigneeUserId,
        dueDate: dueDate || undefined,
      })
    },
    onSuccess: () => {
      onSaved()
      onClose()
    },
    onError: (error: unknown) => {
      setErrors(error instanceof ApiError ? error.messages : ['Could not save the task.'])
    },
  })

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setErrors([])
    if (!title.trim()) {
      setErrors(['Title is required.'])
      return
    }
    mutation.mutate()
  }

  return (
    <Modal isOpen={isOpen} onClose={onClose} title={isEditing ? 'Edit task' : 'New task'}>
      <form onSubmit={handleSubmit} className="flex flex-col gap-4">
        <Input label="Title" value={title} onChange={(event) => setTitle(event.target.value)} required autoFocus />

        <div className="flex flex-col gap-1">
          <label className="text-sm font-medium text-slate-700" htmlFor="task-description">
            Description
          </label>
          <textarea
            id="task-description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            rows={3}
            className="rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 shadow-sm
              focus:outline-none focus:ring-2 focus:ring-slate-900/20"
          />
        </div>

        <div className="grid grid-cols-2 gap-3">
          <Select
            label="Priority"
            value={priority}
            onChange={(event) => setPriority(event.target.value as TaskPriority)}
          >
            {PRIORITY_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </Select>

          <Input
            label="Due date"
            type="date"
            value={dueDate}
            onChange={(event) => setDueDate(event.target.value)}
          />
        </div>

        {isEditing && (
          <Select
            label="Status"
            value={status}
            onChange={(event) => setStatus(event.target.value as TaskItemStatus)}
          >
            {STATUS_COLUMNS.map(({ status: value, label }) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </Select>
        )}

        {canOfferAssignToMe ? (
          <label className="flex items-center gap-2 text-sm text-slate-700">
            <input
              type="checkbox"
              checked={assignToMe}
              onChange={(event) => setAssignToMe(event.target.checked)}
              className="h-4 w-4 rounded border-slate-300"
            />
            Assign this task to me{currentUser ? ` (${currentUser.email})` : ''}
          </label>
        ) : (
          <p className="text-sm text-slate-600">
            Assigned to {task?.assigneeUserId === currentUser?.userId ? 'you' : 'another user'}
          </p>
        )}

        {isEditing && (
          <div className="border-t border-slate-200 pt-4">
            <TaskAttachments taskId={task.id} />
          </div>
        )}

        <ErrorBanner messages={errors} />

        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" isLoading={mutation.isPending}>
            {isEditing ? 'Save changes' : 'Create task'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
