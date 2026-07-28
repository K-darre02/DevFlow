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
