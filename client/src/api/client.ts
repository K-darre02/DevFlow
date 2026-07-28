import { useAuthStore } from '../auth/authStore'

// Matches the ProblemDetails/ValidationProblemDetails shape returned by
// GlobalExceptionHandler and ASP.NET Core's built-in model validation (see
// src/DevFlow.Api/Middleware/GlobalExceptionHandler.cs). Note: error
// dictionary *keys* are the original C# PascalCase property names (e.g.
// "Email"), not camelCased — ASP.NET Core's JSON camelCase naming policy
// applies to object properties, not to validation-error dictionary keys.
export class ApiError extends Error {
  readonly status: number
  readonly errors?: Record<string, string[]>
  /** The `current` field from a 409 response body (TasksController.UpdateTask) — the authoritative current state. */
  readonly conflict?: unknown

  constructor(status: number, message: string, errors?: Record<string, string[]>, conflict?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.errors = errors
    this.conflict = conflict
  }

  /** Flattened, human-readable list — field-level errors if present, else the general message. */
  get messages(): string[] {
    if (this.errors) {
      return Object.values(this.errors).flat()
    }
    return [this.message]
  }
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PATCH' | 'DELETE'
  body?: unknown
  /** Attach the Authorization header. Default true — set false for /auth/* calls. */
  auth?: boolean
  /** Extra headers — e.g. If-Match for optimistic-concurrency updates (see api/tasks.ts). */
  headers?: Record<string, string>
}

export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, auth = true, headers: extraHeaders } = options

  const headers: Record<string, string> = { ...extraHeaders }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  if (auth) {
    const token = useAuthStore.getState().accessToken
    if (token) {
      headers.Authorization = `Bearer ${token}`
    }
  }

  const response = await fetch(`/api${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  if (response.status === 401 && auth) {
    // No refresh-token flow exists yet (a deliberate scope cut — see
    // docs/devflow/05-technical-decisions.md ADR 3 for the target design).
    // A 401 here means the access token is missing, expired, or otherwise
    // invalid; the only correct move is to drop the session so
    // ProtectedRoute redirects to /login, rather than retry or ignore it.
    useAuthStore.getState().logout()
  }

  if (response.status === 204) {
    return undefined as T
  }

  const text = await response.text()
  const data: unknown = text ? JSON.parse(text) : undefined

  if (!response.ok) {
    const problem = data as
      | { title?: string; detail?: string; errors?: Record<string, string[]>; current?: unknown }
      | undefined
    const message = problem?.detail ?? problem?.title ?? `Request failed (${response.status})`
    throw new ApiError(response.status, message, problem?.errors, problem?.current)
  }

  return data as T
}
