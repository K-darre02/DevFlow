import type { ButtonHTMLAttributes } from 'react'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'danger' | 'link' | 'link-danger'
  isLoading?: boolean
}

// link/link-danger carry their own padding/shape (none — no rounded-md
// px-4 py-2) rather than sharing the base below, since they're for compact
// contexts (a row in a list, a card footer) where a full-size button
// doesn't fit. Formalizes a pattern that was otherwise being hand-rolled
// per callsite (NotificationBell's "Mark all as read", DashboardPage's
// "View all").
const variantClasses: Record<NonNullable<ButtonProps['variant']>, string> = {
  primary: 'rounded-md px-4 py-2 bg-slate-900 text-white hover:bg-slate-700 focus-visible:outline-slate-900',
  secondary:
    'rounded-md px-4 py-2 bg-white text-slate-900 border border-slate-300 hover:bg-slate-50 focus-visible:outline-slate-400',
  danger: 'rounded-md px-4 py-2 bg-red-600 text-white hover:bg-red-500 focus-visible:outline-red-600',
  link: 'text-slate-600 hover:text-slate-900 focus-visible:outline-slate-400',
  'link-danger': 'text-red-600 hover:text-red-800 focus-visible:outline-red-600',
}

export function Button({
  variant = 'primary',
  isLoading = false,
  disabled,
  className = '',
  children,
  ...rest
}: ButtonProps) {
  return (
    <button
      disabled={disabled || isLoading}
      className={`inline-flex items-center justify-center gap-2 text-sm font-medium
        transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2
        disabled:cursor-not-allowed disabled:opacity-50 ${variantClasses[variant]} ${className}`}
      {...rest}
    >
      {isLoading ? 'Please wait…' : children}
    </button>
  )
}
