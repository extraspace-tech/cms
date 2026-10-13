import { cloneElement, type ReactElement } from 'react'
import { Label } from '@/components/ui/label'

type Control = ReactElement<{ 'aria-invalid'?: boolean; 'aria-describedby'?: string }>

/** `children` is the one control with id `htmlFor`; an error is tied to it for screen readers. */
export function FormField({ label, htmlFor, error, children }: { label: string; htmlFor: string; error?: string; children: Control }) {
  const errorId = `${htmlFor}-error`
  return (
    <div className="space-y-1.5">
      <Label htmlFor={htmlFor}>{label}</Label>
      {error ? cloneElement(children, { 'aria-invalid': true, 'aria-describedby': errorId }) : children}
      {error ? <p id={errorId} role="alert" className="text-xs text-destructive">{error}</p> : null}
    </div>
  )
}
