import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { AuthResponse, TenantRole } from '../api/types'
import { isTokenExpired } from './jwt'

interface AuthUser {
  userId: string
  tenantId: string
  email: string
  role: TenantRole
}

interface AuthState {
  accessToken: string | null
  user: AuthUser | null
  isAuthenticated: boolean
  login: (auth: AuthResponse) => void
  logout: () => void
}

// Persisted to localStorage — a deliberate deviation from
// docs/devflow/04-security.md §4 ("access token held in memory... not
// localStorage, to reduce XSS exfiltration risk"). That design assumes a
// refresh-token cookie silently re-establishes a session on page load;
// no refresh flow exists yet (ADR 3 in Technical Decisions), so
// memory-only storage would log a user out on every browser refresh —
// not a workable "product flow" for this phase. Revisit once refresh
// tokens land.
export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      user: null,
      isAuthenticated: false,
      login: (auth) =>
        set({
          accessToken: auth.accessToken,
          user: { userId: auth.userId, tenantId: auth.tenantId, email: auth.email, role: auth.role },
          isAuthenticated: true,
        }),
      logout: () => set({ accessToken: null, user: null, isAuthenticated: false }),
    }),
    {
      name: 'devflow-auth',
      // Drop a stale/expired token on load rather than trusting whatever's
      // sitting in localStorage — avoids sending a request that's certain
      // to 401.
      onRehydrateStorage: () => (state) => {
        if (state?.accessToken && isTokenExpired(state.accessToken)) {
          state.logout()
        }
      },
    },
  ),
)
