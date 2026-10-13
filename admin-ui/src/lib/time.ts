const format = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })

/** A server instant in the browser's time zone. */
export function formatInstant(iso: string): string {
  return format.format(new Date(iso))
}

export function zoneName(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone
}

/** A `datetime-local` value (browser time, no zone) as an instant with Z; the audit filter needs an offset. */
export function localInputToInstant(value: string): string | undefined {
  if (!value) return undefined
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString()
}
