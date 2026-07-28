import { Navigate, Outlet } from 'react-router-dom'
import { useAuthStore } from './authStore'

/** Wraps routes that require a session — unauthenticated visitors go to /login. */
export function ProtectedRoute() {
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated)
  return isAuthenticated ? <Outlet /> : <Navigate to="/login" replace />
}

/** Wraps /login and /register — an already-authenticated visitor is sent to the dashboard. */
export function PublicOnlyRoute() {
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated)
  return isAuthenticated ? <Navigate to="/" replace /> : <Outlet />
}
