import { Badge } from '@/components/ui/badge'

/** The demo's cell rules (S1 research crosscutting §1.1): empty = grey dash, booleans = Yes/No badge, statuses = badge. */
export function Dash({ value }: { value: string | number | null | undefined }) {
  return value === null || value === undefined || value === '' ? <span className="text-muted-foreground">—</span> : <>{value}</>
}

export function YesNo({ value }: { value: boolean }) {
  return <Badge variant={value ? 'default' : 'secondary'}>{value ? 'Yes' : 'No'}</Badge>
}

export function StatusBadge({ value }: { value: string }) {
  return <Badge variant="outline">{value}</Badge>
}
