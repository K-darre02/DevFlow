import type { ReactNode } from 'react'

type BadgeColor = 'gray' | 'blue' | 'orange' | 'red' | 'green'

const colorClasses: Record<BadgeColor, string> = {
  gray: 'bg-slate-100 text-slate-700',
  blue: 'bg-blue-100 text-blue-700',
  orange: 'bg-orange-100 text-orange-700',
  red: 'bg-red-100 text-red-700',
  green: 'bg-green-100 text-green-700',
}

interface BadgeProps {
  children: ReactNode
  color?: BadgeColor
}

export function Badge({ children, color = 'gray' }: BadgeProps) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${colorClasses[color]}`}
    >
      {children}
    </span>
  )
}
