import { apiFetch } from './client'
import type { AuthResponse, LoginRequest, RegisterRequest } from './types'

export function register(request: RegisterRequest) {
  return apiFetch<AuthResponse>('/auth/register', { method: 'POST', body: request, auth: false })
}

export function login(request: LoginRequest) {
  return apiFetch<AuthResponse>('/auth/login', { method: 'POST', body: request, auth: false })
}
