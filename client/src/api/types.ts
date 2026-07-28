// Mirrors the C# contracts in src/DevFlow.Api/Contracts. ASP.NET Core's
// default JSON options camelCase object properties, so these match that —
// except validation error dictionary *keys* (see ApiError below), which
// come through as the original PascalCase C# property/field names.

export interface RegisterRequest {
  tenantName: string
  email: string
  password: string
}

export interface LoginRequest {
  email: string
  password: string
}

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  tenantId: string
  userId: string
  email: string
}

export interface Project {
  id: string
  tenantId: string
  name: string
  isArchived: boolean
  createdAt: string
}

export interface CreateProjectRequest {
  name: string
}

export interface UpdateProjectRequest {
  name?: string
  isArchived?: boolean
}

// Mirrors DevFlow.Domain.Enums.TaskItemStatus / TaskPriority — serialized as
// strings (JsonStringEnumConverter, see DevFlow.Api/DependencyInjection.cs).
export type TaskItemStatus = 'Backlog' | 'ToDo' | 'InProgress' | 'InReview' | 'Done'
export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Urgent'

export interface Task {
  id: string
  projectId: string
  assigneeUserId: string | null
  title: string
  description: string | null
  status: TaskItemStatus
  priority: TaskPriority
  dueDate: string | null
  completedAt: string | null
  createdAt: string
  /** Optimistic concurrency token — send back as the If-Match header on update. */
  version: number
}

export interface CreateTaskRequest {
  projectId: string
  title: string
  description?: string
  priority: TaskPriority
  assigneeUserId?: string
  dueDate?: string
}

// PATCH semantics: only included (non-undefined) fields change. See
// DevFlow.Application/Tasks/UpdateTaskInput.cs — there's no way to explicitly
// clear assigneeUserId/dueDate/description back to empty through this
// endpoint, only to set a new value. Same limitation here.
export interface UpdateTaskRequest {
  title?: string
  description?: string
  status?: TaskItemStatus
  priority?: TaskPriority
  assigneeUserId?: string
  dueDate?: string
}
