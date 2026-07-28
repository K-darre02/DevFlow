interface BarChartRow {
  label: string
  count: number
  colorClass: string
}

interface BarChartProps {
  rows: BarChartRow[]
}

// Plain CSS bars, not a charting library — matches this project's existing
// hand-rolled UI primitives (no shadcn/ui, no icon library) rather than
// pulling in a new dependency for a handful of bars.
export function BarChart({ rows }: BarChartProps) {
  const max = Math.max(1, ...rows.map((row) => row.count))

  return (
    <div className="flex flex-col gap-2.5">
      {rows.map((row) => (
        <div key={row.label} className="flex items-center gap-3">
          <span className="w-20 flex-shrink-0 text-xs text-slate-600">{row.label}</span>
          <div className="h-2 flex-1 overflow-hidden rounded-full bg-slate-100">
            <div
              className={`h-2 rounded-full transition-all ${row.colorClass}`}
              style={{ width: `${(row.count / max) * 100}%` }}
            />
          </div>
          <span className="w-6 flex-shrink-0 text-right text-xs font-medium text-slate-700">{row.count}</span>
        </div>
      ))}
    </div>
  )
}
