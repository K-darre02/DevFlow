import { useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, type HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useAuthStore } from '../auth/authStore'
import { registerNotificationEventHandlers } from './notificationEvents'
import { registerTaskEventHandlers } from './taskEvents'

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

// Mounted once, in AppLayout — which only ever renders on an authenticated
// route (ProtectedRoute), so "connect when the user logs in" falls out of
// the component's own mount lifecycle rather than needing separate
// login/logout event wiring. Unmounting (navigating away via logout) tears
// the connection down.
export function useRealtimeConnection(): ConnectionStatus {
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated)
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<ConnectionStatus>('connecting')
  const connectionRef = useRef<HubConnection | null>(null)

  useEffect(() => {
    if (!isAuthenticated) {
      return
    }

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/tasks', {
        // Read fresh from the store on every (re)connect attempt rather than
        // closing over a single token — matters most on reconnect, where a
        // stale closure could retry with an already-invalid value.
        accessTokenFactory: () => useAuthStore.getState().accessToken ?? '',
      })
      .withAutomaticReconnect()
      .build()

    connectionRef.current = connection
    registerTaskEventHandlers(connection, queryClient)
    registerNotificationEventHandlers(connection, queryClient)

    connection.onreconnecting(() => setStatus('reconnecting'))
    connection.onreconnected(() => setStatus('connected'))
    connection.onclose(() => setStatus('disconnected'))

    setStatus('connecting')
    connection
      .start()
      .then(() => setStatus('connected'))
      .catch(() => setStatus('disconnected'))

    return () => {
      connectionRef.current = null
      void connection.stop()
    }
  }, [isAuthenticated, queryClient])

  return isAuthenticated ? status : 'disconnected'
}
