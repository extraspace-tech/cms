import { useState } from 'react'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { Dash } from '@/components/cells'
import { columnsFor, DataTable } from '@/components/DataTable'
import { ErrorNote, toastError } from '@/components/ErrorNote'
import { Button } from '@/components/ui/button'
import { useSession } from '@/features/auth/session'
import { formatInstant, zoneName } from '@/lib/time'
import { useStartUnitSync, useUnitSync, type UnitSyncStatus } from './api'

const helper = columnsFor<UnitSyncStatus>()
const columns = helper.columns([
  helper.accessor('locationCode', { header: 'Site', cell: (c) => `${c.getValue()} · ${c.row.original.siteName}` }),
  helper.accessor('units', { header: 'Units' }),
  helper.accessor('lastSyncedAt', { header: `Last synced (${zoneName()})`, cell: (c) => { const at = c.getValue(); return at ? formatInstant(at) : 'Never' } }),
  helper.accessor('lastError', { header: 'Last error', cell: (c) => {
    const error = c.getValue()
    const at = c.row.original.lastAttemptAt
    return error ? <span className="text-destructive">{error}{at ? <span className="text-muted-foreground"> · {formatInstant(at)}</span> : null}</span> : <Dash value={null} />
  } }),
])

/** QOL addition (spec §15): the last SiteLink pull per site, its error, and "Sync now" / "Full resync" for sites.manage. */
export function UnitSyncPanel() {
  const status = useUnitSync()
  const start = useStartUnitSync()
  const canSync = useSession().can('sites.manage')
  const [full, setFull] = useState<UnitSyncStatus | null>(null)
  return (
    <section className="space-y-3 pt-4">
      <h2 className="text-base font-semibold">SiteLink sync</h2>
      {status.isError ? <ErrorNote error={status.error} /> : null}
      <DataTable
        columns={columns}
        data={status.data ?? []}
        rowId={(s) => s.siteId}
        empty={status.data ? 'No sites yet' : status.isPending ? 'Loading…' : null}
        rowActions={canSync ? (s) => (
          <div className="flex justify-end gap-2">
            <Button size="sm" variant="outline" disabled={start.isPending} aria-label={`Sync now ${s.locationCode}`}
              onClick={() => start.mutate({ siteId: s.siteId, full: false }, { onError: toastError })}>Sync now</Button>
            {/* reset() would drop a pending Sync now and its error toast. */}
            <Button size="sm" variant="outline" disabled={start.isPending} aria-label={`Full resync ${s.locationCode}`} onClick={() => { start.reset(); setFull(s) }}>Full resync</Button>
          </div>
        ) : undefined}
      />
      <ConfirmDialog
        open={full !== null}
        title="Full resync?"
        description={`Pulls every unit of ${full?.locationCode ?? ''} from SiteLink again. Use it when a normal sync missed a change.`}
        confirmLabel="Start"
        pending={start.isPending}
        error={start.error}
        onConfirm={() => { if (full) start.mutate({ siteId: full.siteId, full: true }, { onSuccess: () => setFull(null) }) }}
        onCancel={() => { start.reset(); setFull(null) }}
      />
    </section>
  )
}
