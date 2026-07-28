import type { SelectHTMLAttributes } from 'react'
import { useId } from 'react'

interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label: string
  error?: string
  /** Keeps the label in the accessibility tree (still the select's accessible name) but visually hidden — for compact contexts (e.g. an inline per-row control) where a visible label line doesn't fit. */
  hideLabel?: boolean
}

export function Select({ label, error, hideLabel, id, className = '', children, ...rest }: SelectProps) {
  const generatedId = useId()
  const selectId = id ?? generatedId

  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={selectId} className={hideLabel ? 'sr-only' : 'text-sm font-medium text-slate-700'}>
        {label}
      </label>
      <select
        id={selectId}
        className={`rounded-md border px-3 py-2 text-sm text-slate-900 shadow-sm
          focus:outline-none focus:ring-2 focus:ring-slate-900/20
          ${error ? 'border-red-400' : 'border-slate-300'} ${className}`}
        aria-invalid={error ? true : undefined}
        {...rest}
      >
        {children}
      </select>
      {error && <p className="text-sm text-red-600">{error}</p>}
    </div>
  )
}
