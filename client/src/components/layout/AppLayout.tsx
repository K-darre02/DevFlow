import { Outlet } from 'react-router-dom'
import { useAuthStore } from '../../auth/authStore'
import { Button } from '../ui/Button'

export function AppLayout() {
  const user = useAuthStore((state) => state.user)
  const logout = useAuthStore((state) => state.logout)

  return (
    <div className="min-h-screen bg-slate-50">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-5xl items-center justify-between px-6 py-4">
          <span className="text-lg font-semibold text-slate-900">DevFlow</span>
          <div className="flex items-center gap-4">
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
