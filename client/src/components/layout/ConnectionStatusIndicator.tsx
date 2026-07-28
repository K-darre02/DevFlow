import type { ConnectionStatus } from '../../realtime/useRealtimeConnection'

const STATUS_DISPLAY: Record<ConnectionStatus, { label: string; dotColor: string }> = {
  connected: { label: 'Live', dotColor: 'bg-green-500' },
  connecting: { label: 'Connecting…', dotColor: 'bg-amber-400' },
  reconnecting: { label: 'Reconnecting…', dotColor: 'bg-amber-400' },
  disconnected: { label: 'Offline', dotColor: 'bg-slate-300' },
}

interface ConnectionStatusIndicatorProps {
  status: ConnectionStatus
}

export function ConnectionStatusIndicator({ status }: ConnectionStatusIndicatorProps) {
  const { label, dotColor } = STATUS_DISPLAY[status]

  return (
    <span className="flex items-center gap-1.5 text-xs text-slate-500" title={`Real-time updates: ${label}`}>
      <span className={`h-2 w-2 rounded-full ${dotColor} ${status === 'connecting' || status === 'reconnecting' ? 'animate-pulse' : ''}`} aria-hidden="true" />
      {label}
    </span>
  )
}
