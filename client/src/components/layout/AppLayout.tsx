import { useState } from 'react'
import { Link, Outlet } from 'react-router-dom'
import { useAuthStore } from '../../auth/authStore'
import { useRealtimeConnection } from '../../realtime/useRealtimeConnection'
import { Button } from '../ui/Button'
import { ConnectionStatusIndicator } from './ConnectionStatusIndicator'
import { GlobalSearch } from './GlobalSearch'
import { NotificationBell } from './NotificationBell'

const NAV_LINKS = [
  { to: '/', label: 'Dashboard' },
  { to: '/projects', label: 'Projects' },
  { to: '/team', label: 'Team' },
  { to: '/activity', label: 'Activity' },
]

export function AppLayout() {
  const user = useAuthStore((state) => state.user)
  const logout = useAuthStore((state) => state.logout)
  const connectionStatus = useRealtimeConnection()
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false)

  return (
    <div className="min-h-screen bg-slate-50">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-5xl items-center justify-between px-4 py-4 sm:px-6">
          <div className="flex items-center gap-6">
            <Link to="/" className="text-lg font-semibold text-slate-900">
              DevFlow
            </Link>
            <nav className="hidden items-center gap-6 md:flex">
              {NAV_LINKS.map((link) => (
                <Link key={link.to} to={link.to} className="text-sm font-medium text-slate-600 hover:text-slate-900">
                  {link.label}
                </Link>
              ))}
            </nav>
          </div>

          <div className="hidden items-center gap-4 md:flex">
            <GlobalSearch />
            <ConnectionStatusIndicator status={connectionStatus} />
            <NotificationBell />
            {user && <span className="text-sm text-slate-600">{user.email}</span>}
            <Button variant="secondary" onClick={logout}>
              Log out
            </Button>
          </div>

          <button
            type="button"
            onClick={() => setIsMobileMenuOpen((open) => !open)}
            aria-label={isMobileMenuOpen ? 'Close menu' : 'Open menu'}
            aria-expanded={isMobileMenuOpen}
            className="rounded-md p-2 text-slate-500 hover:bg-slate-100 hover:text-slate-700 md:hidden"
          >
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="h-5 w-5" aria-hidden="true">
              {isMobileMenuOpen ? (
                <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
              ) : (
                <path strokeLinecap="round" strokeLinejoin="round" d="M4 6h16M4 12h16M4 18h16" />
              )}
            </svg>
          </button>
        </div>

        {isMobileMenuOpen && (
          <div className="flex flex-col gap-4 border-t border-slate-200 px-4 py-4 md:hidden">
            <nav className="flex flex-col gap-3">
              {NAV_LINKS.map((link) => (
                <Link
                  key={link.to}
                  to={link.to}
                  onClick={() => setIsMobileMenuOpen(false)}
                  className="text-sm font-medium text-slate-600 hover:text-slate-900"
                >
                  {link.label}
                </Link>
              ))}
            </nav>

            <GlobalSearch className="w-full" />

            <div className="flex items-center justify-between border-t border-slate-100 pt-4">
              <ConnectionStatusIndicator status={connectionStatus} />
              <NotificationBell />
            </div>

            {user && <span className="text-sm text-slate-600">{user.email}</span>}
            <Button variant="secondary" onClick={logout}>
              Log out
            </Button>
          </div>
        )}
      </header>

      <main className="mx-auto max-w-5xl px-4 py-8 sm:px-6">
        <Outlet />
      </main>
    </div>
  )
}
