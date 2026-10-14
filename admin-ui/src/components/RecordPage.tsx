import type { ReactNode } from 'react'
import { Link } from 'react-router'

/** The demo's record page frame: "← Back to …", one card, title, the read-only note. */
export function RecordPage({ backTo, backLabel, title, readOnly, children }: { backTo: string; backLabel: string; title: string; readOnly?: boolean; children: ReactNode }) {
  return (
    <section className="max-w-2xl space-y-4">
      <Link to={backTo} className="text-sm text-muted-foreground hover:text-foreground">← Back to {backLabel}</Link>
      <div className="space-y-5 rounded-xl border bg-card p-6">
        <h1 className="text-lg font-semibold">{title}</h1>
        {readOnly ? <p className="text-sm text-muted-foreground">This record is read-only.</p> : null}
        {children}
      </div>
    </section>
  )
}
