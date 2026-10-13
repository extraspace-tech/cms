function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function show(value: unknown): string {
  if (value === null || value === undefined) return '—'
  return typeof value === 'string' ? value : JSON.stringify(value)
}

/** `changes` is `{ field: { from, to } }`, written by the server's audit interceptor. Anything else is shown as raw JSON. */
export function Changes({ changes }: { changes: unknown }) {
  if (changes === null || changes === undefined) return <span>—</span>
  // Table cells do not wrap by default: a long value must break instead of widening the table.
  if (!isRecord(changes)) return <code className="text-xs whitespace-normal break-all">{JSON.stringify(changes)}</code>
  return (
    <dl className="space-y-0.5 text-xs whitespace-normal break-all">
      {Object.entries(changes).map(([field, change]) => (
        <div key={field} className="flex gap-2">
          <dt className="shrink-0 font-mono text-muted-foreground">{field}</dt>
          <dd className="min-w-0">{isRecord(change) ? `${show(change.from)} → ${show(change.to)}` : show(change)}</dd>
        </div>
      ))}
    </dl>
  )
}
