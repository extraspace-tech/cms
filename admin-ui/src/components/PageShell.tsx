import type { ReactNode } from 'react'

export function PageShell({ title, actions, children }: { title: string; actions?: ReactNode; children?: ReactNode }) {
  return (
    <section className="space-y-4">
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-lg font-semibold">{title}</h1>
        <div className="flex items-center gap-2">{actions}</div>
      </div>
      {children}
    </section>
  )
}
