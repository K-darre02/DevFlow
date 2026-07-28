import { Link, Outlet } from 'react-router-dom'
import { useAuthStore } from '../../auth/authStore'
import { useRealtimeConnection } from '../../realtime/useRealtimeConnection'
import { Button } from '../ui/Button'
import { ConnectionStatusIndicator } from './ConnectionStatusIndicator'

export function AppLayout() {
  const user = useAuthStore((state) => state.user)
  const logout = useAuthStore((state) => state.logout)
  const connectionStatus = useRealtimeConnection()

  return (
    <div className="min-h-screen bg-slate-50">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-5xl items-center justify-between px-6 py-4">
          <div className="flex items-center gap-6">
            <Link to="/" className="text-lg font-semibold text-slate-900">
              DevFlow
            </Link>
            <Link to="/team" className="text-sm font-medium text-slate-600 hover:text-slate-900">
              Team
            </Link>
            <Link to="/activity" className="text-sm font-medium text-slate-600 hover:text-slate-900">
              Activity
            </Link>
          </div>
          <div className="flex items-center gap-4">
            <ConnectionStatusIndicator status={connectionStatus} />
            {user && <span className="text-sm text-slate-600">{user.email}</span>}
            <Button variant="secondary" onClick={logout}>
              Log out
            </Button>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-5xl px-6 py-8">
        <Outlet />
      </main>
    </div>
  )
}
