import type { QueryClient } from '@tanstack/react-query'
import type { HubConnection } from '@microsoft/signalr'
import type { Project, Task, TaskItemStatus } from '../api/types'

// Idempotent, id-keyed list update — safe to apply the same event twice (a
// SignalR echo of a change the current tab already made optimistically,
// or an out-of-order redelivery) without producing a duplicate row. This is
// the core defense against "duplicate updates caused by optimistic UI
// followed by SignalR events": every handler below goes through one of
// these three functions rather than blindly appending.
function upsertById<T extends { id: string }>(list: T[] | undefined, item: T): T[] | undefined {
  if (!list) return list // don't materialize a cache entry for a list nobody is viewing
  const index = list.findIndex((existing) => existing.id === item.id)
  if (index === -1) return [...list, item]
  const next = list.slice()
  next[index] = item
  return next
}

function removeById<T extends { id: string }>(list: T[] | undefined, id: string): T[] | undefined {
  if (!list) return list
  return list.filter((existing) => existing.id !== id)
}

// Tasks additionally carry a monotonically increasing `version` (the same
// optimistic-concurrency token the REST API uses). A SignalR event is only
// ever a *report* of a write that already happened — if the cached copy is
// already at or past that version, this tab either made the change itself
// (optimistic update + its own event echo) or already applied a newer
// event; either way, re-applying would be redundant work at best and a
// step backwards at worst if messages arrive out of order.
function upsertTaskIfNewer(list: Task[] | undefined, task: Task): Task[] | undefined {
  if (!list) return list
  const existing = list.find((item) => item.id === task.id)
  if (existing && existing.version >= task.version) return list
  return upsertById(list, task)
}

export function registerTaskEventHandlers(connection: HubConnection, queryClient: QueryClient): void {
  connection.on('project.created', (project: Project) => {
    queryClient.setQueryData<Project[]>(['projects'], (old) => upsertById(old, project))
  })

  connection.on('project.archived', (project: Project) => {
    // ['projects'] always represents the non-archived list (see
    // ProjectsPage — it never passes includeArchived), so an archived
    // project is removed from it rather than updated in place.
    queryClient.setQueryData<Project[]>(['projects'], (old) => removeById(old, project.id))
    queryClient.setQueryData<Project>(['project', project.id], project)
  })

  connection.on('task.created', (task: Task) => {
    queryClient.setQueryData<Task[]>(['tasks', task.projectId], (old) => upsertById(old, task))
  })

  connection.on('task.updated', (task: Task) => {
    queryClient.setQueryData<Task[]>(['tasks', task.projectId], (old) => upsertTaskIfNewer(old, task))
  })

  // fromStatus isn't currently used for anything beyond what `task` itself
  // already carries (its new Status) — kept in the payload since a future
  // "Alice moved a card from X to Y" activity feed would need it, and it
  // costs nothing to receive now.
  connection.on('task.moved', (task: Task, _fromStatus: TaskItemStatus) => {
    queryClient.setQueryData<Task[]>(['tasks', task.projectId], (old) => upsertTaskIfNewer(old, task))
  })

  connection.on('task.assigned', (task: Task) => {
    queryClient.setQueryData<Task[]>(['tasks', task.projectId], (old) => upsertTaskIfNewer(old, task))
  })

  connection.on('task.completed', (task: Task) => {
    queryClient.setQueryData<Task[]>(['tasks', task.projectId], (old) => upsertTaskIfNewer(old, task))
  })

  connection.on('task.deleted', (taskId: string, projectId: string) => {
    queryClient.setQueryData<Task[]>(['tasks', projectId], (old) => removeById(old, taskId))
  })
}
