import { apiFetch } from './client'
import type { CreateProjectRequest, Project, UpdateProjectRequest } from './types'

export function getProjects(includeArchived = false) {
  const query = includeArchived ? '?includeArchived=true' : ''
  return apiFetch<Project[]>(`/projects${query}`)
}

export function getProject(id: string) {
  return apiFetch<Project>(`/projects/${id}`)
}

export function createProject(request: CreateProjectRequest) {
  return apiFetch<Project>('/projects', { method: 'POST', body: request })
}

export function updateProject(id: string, request: UpdateProjectRequest) {
  return apiFetch<Project>(`/projects/${id}`, { method: 'PATCH', body: request })
}

export function deleteProject(id: string) {
  return apiFetch<void>(`/projects/${id}`, { method: 'DELETE' })
}
