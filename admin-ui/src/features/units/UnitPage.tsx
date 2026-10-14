import { Fragment, type ReactNode } from 'react'
import { useParams } from 'react-router'
import { YesNo } from '@/components/cells'
import { ErrorNote } from '@/components/ErrorNote'
import { RecordPage } from '@/components/RecordPage'
import { formatMoney } from '@/lib/money'
import { formatInstant } from '@/lib/time'
import { useUnit } from './api'

/** The demo's read-only unit record (fields in its order), plus when SiteLink deleted it. */
export function UnitPage() {
  const { id } = useParams()
  const unit = useUnit(id!)
  if (!unit.data) return unit.isError ? <ErrorNote error={unit.error} /> : <p className="text-sm text-muted-foreground">Loading…</p>
  const u = unit.data
  const rows: [string, ReactNode][] = [
    ['Unit Name', u.unitName],
    ['Site', u.siteName],
    ['Storage Type', u.storageTypeName ?? '—'],
    ['Unit Size', u.unitSizeLabel ?? '—'],
    ['Width', u.width],
    ['Length', u.length],
    ['Area (sq ft)', u.areaSqft],
    ['Floor', u.floor ?? '—'],
    ['Climate', <YesNo value={u.climate} />],
    ['Standard Rate', formatMoney(u.standardRate, u.currency)],
    ['Web Rate', formatMoney(u.webRate, u.currency)],
    ['Rented', <YesNo value={u.rented} />],
    ['Available', <YesNo value={u.available} />],
    ['Deleted in SiteLink', u.deletedAt ? formatInstant(u.deletedAt) : '—'],
    ['Last Synced At', u.lastSyncedAt ? formatInstant(u.lastSyncedAt) : '—'],
  ]
  return (
    <RecordPage backTo="/units" backLabel="Units" title="Unit" readOnly>
      <dl className="grid grid-cols-[12rem_1fr] gap-x-4 gap-y-2 text-sm">
        {rows.map(([label, value]) => (
          <Fragment key={label}>
            <dt className="text-muted-foreground">{label}</dt>
            <dd>{value}</dd>
          </Fragment>
        ))}
      </dl>
    </RecordPage>
  )
}
