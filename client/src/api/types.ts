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

// Mirrors DevFlow.Domain.Enums.TenantRole — serialized as a string
// (JsonStringEnumConverter, see DevFlow.Api/DependencyInjection.cs).
export type TenantRole = 'Owner' | 'Admin' | 'Member'

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  tenantId: string
  userId: string
  email: string
  role: TenantRole
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

export interface TeamMember {
  id: string
  userId: string
  email: string
  role: TenantRole
  joinedAt: string
}

export interface InviteMemberRequest {
  email: string
  role: TenantRole
}

// Token is the raw, one-time invitation token — see
// DevFlow.Api/Contracts/Team/InvitationResponse.cs. There is no email
// delivery yet, so the inviter shares the accept link out of band.
export interface Invitation {
  id: string
  email: string
  role: TenantRole
  expiresAt: string
  token: string
}

export interface AcceptInvitationRequest {
  token: string
  password: string
}

export interface ChangeRoleRequest {
  role: TenantRole
}

// Mirrors DevFlow.Domain.Enums.ActivityType / ActivityEntityType.
export type ActivityType =
  | 'ProjectCreated'
  | 'ProjectArchived'
  | 'TaskCreated'
  | 'TaskUpdated'
  | 'TaskDeleted'
  | 'TaskMoved'
  | 'TaskAssigned'
  | 'TaskCompleted'
  | 'MemberInvited'
  | 'MemberJoined'
  | 'RoleChanged'
  | 'MemberRemoved'

export type ActivityEntityType = 'Project' | 'Task' | 'Invitation' | 'TeamMember'

export interface Activity {
  id: string
  activityType: ActivityType
  entityType: ActivityEntityType
  entityId: string
  description: string
  /** Raw JSON string, shape varies by activityType — parse only where needed (e.g. TaskMoved's fromStatus/toStatus). */
  metadata: string | null
  userId: string | null
  /** Resolved server-side via a join — null only if userId is null. */
  userEmail: string | null
  createdAt: string
}

export interface PagedResponse<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

export interface GetActivitiesParams {
  page?: number
  pageSize?: number
  entityType?: ActivityEntityType
  userId?: string
  activityType?: ActivityType
}

// Mirrors DevFlow.Domain.Enums.NotificationType.
export type NotificationType = 'TaskAssigned' | 'TaskMentioned' | 'ProjectChanged' | 'TeamInvitationAccepted' | 'RoleChanged'

export interface Notification {
  id: string
  type: NotificationType
  title: string
  message: string
  isRead: boolean
  createdAt: string
}

export interface Attachment {
  id: string
  taskId: string
  fileName: string
  contentType: string
  size: number
  uploadedByUserId: string
  uploadedByEmail: string | null
  createdAt: string
}

export interface GetNotificationsParams {
  page?: number
  pageSize?: number
}

export interface UnreadCountResponse {
  count: number
}

export interface OverdueTask {
  id: string
  title: string
  projectId: string
  projectName: string
  dueDate: string
  priority: TaskPriority
}

export interface DashboardSummary {
  projectCount: number
  taskCount: number
  completedTaskCount: number
  overdueTaskCount: number
  tasksByStatus: Partial<Record<TaskItemStatus, number>>
  tasksByPriority: Partial<Record<TaskPriority, number>>
  recentActivity: Activity[]
  overdueTasks: OverdueTask[]
}
