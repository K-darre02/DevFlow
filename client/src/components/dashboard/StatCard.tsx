import { Card } from '../ui/Card'

interface StatCardProps {
  label: string
  value: number
  accentClass: string
}

export function StatCard({ label, value, accentClass }: StatCardProps) {
  return (
    <Card className="relative overflow-hidden">
      <span className={`absolute inset-x-0 top-0 h-1 ${accentClass}`} aria-hidden="true" />
      <p className="text-sm text-slate-500">{label}</p>
      <p className="mt-1 text-3xl font-semibold text-slate-900">{value.toLocaleString()}</p>
    </Card>
  )
}
