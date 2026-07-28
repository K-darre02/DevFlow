import { apiFetch } from './client'
import type { CreateTaskRequest, Task, TaskItemStatus, UpdateTaskRequest } from './types'

export interface GetTasksParams {
  projectId?: string
  status?: TaskItemStatus
  assigneeUserId?: string
}

export function getTasks(params: GetTasksParams = {}) {
  const query = new URLSearchParams()
  if (params.projectId) query.set('projectId', params.projectId)
  if (params.status) query.set('status', params.status)
  if (params.assigneeUserId) query.set('assigneeUserId', params.assigneeUserId)
  const queryString = query.toString()
  return apiFetch<Task[]>(`/tasks${queryString ? `?${queryString}` : ''}`)
}

export function createTask(request: CreateTaskRequest) {
  return apiFetch<Task>('/tasks', { method: 'POST', body: request })
}

// expectedVersion is the Task.version last seen by the client — sent as
// If-Match so the server can detect a conflicting concurrent change (see
// docs/devflow/06-engineering-challenges.md §2). A stale version throws an
// ApiError with status 409 and `conflict` set to the task's actual current
// state (TasksController.UpdateTask's response body).
export function updateTask(id: string, request: UpdateTaskRequest, expectedVersion: number) {
  return apiFetch<Task>(`/tasks/${id}`, {
    method: 'PATCH',
    body: request,
    headers: { 'If-Match': String(expectedVersion) },
  })
}

export function deleteTask(id: string) {
  return apiFetch<void>(`/tasks/${id}`, { method: 'DELETE' })
}
