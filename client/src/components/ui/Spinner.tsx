interface SpinnerProps {
  label?: string
}

export function Spinner({ label = 'Loading…' }: SpinnerProps) {
  return (
    <div className="flex items-center justify-center gap-2 py-8 text-sm text-slate-500">
      <span
        className="h-4 w-4 animate-spin rounded-full border-2 border-slate-300 border-t-slate-600"
        aria-hidden="true"
      />
      {label}
    </div>
  )
}
