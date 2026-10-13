import { useState, type FormEvent } from 'react'
import { columnsFor, DataTable } from '@/components/DataTable'
import { ErrorNote } from '@/components/ErrorNote'
import { FormField } from '@/components/FormField'
import { NativeSelect } from '@/components/NativeSelect'
import { PageShell } from '@/components/PageShell'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useSession } from '@/features/auth/session'
import { useStaff } from '@/features/staff/api'
import { formatInstant, localInputToInstant, zoneName } from '@/lib/time'
import { PAGE_SIZE, useAudit, type AuditFilter, type AuditItem } from './api'
import { Changes } from './Changes'

// The audit log names a record by its table. Add a line here when a later feature audits a new table.
const recordTypes = [
  { value: 'staff_user', label: 'Staff member' },
  { value: 'role', label: 'Role' },
]

const helper = columnsFor<AuditItem>()
const columns = helper.columns([
  helper.accessor('occurredAt', { header: `When (${zoneName()})`, cell: (c) => formatInstant(c.getValue()) }),
  helper.accessor('staffEmail', { header: 'Who' }),
  helper.accessor('action', { header: 'Action', cell: (c) => <span className="font-mono text-xs">{c.getValue()}</span> }),
  helper.accessor('entityType', { header: 'Record', cell: ({ row }) => (
    <div className="text-xs">
      <div>{row.original.entityType}</div>
      <div className="font-mono text-muted-foreground">{row.original.entityId}</div>
    </div>
  ) }),
  helper.accessor('changes', { header: 'Changes', cell: ({ row }) => <Changes changes={row.original.changes} /> }),
])

function StaffFilter() {
  const staff = useStaff()
  return (
    <>
      <FormField label="Staff" htmlFor="audit-staff">
        <NativeSelect id="audit-staff" name="staffId" defaultValue="">
          <option value="">Anyone</option>
          {(staff.data ?? []).map((member) => <option key={member.id} value={member.id}>{member.email}</option>)}
        </NativeSelect>
      </FormField>
      {staff.isError ? <ErrorNote error={staff.error} /> : null}
    </>
  )
}

export function AuditPage() {
  const { can } = useSession()
  const [filter, setFilter] = useState<AuditFilter>({})
  const [page, setPage] = useState(1)
  const audit = useAudit(filter, page)
  const total = audit.data?.total ?? 0
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))
  // The rows and the total on screen are still the previous answer: no paging on an old total.
  const loading = audit.isPlaceholderData
  const none = total > 0 ? 'No entries on this page.' : 'No entries match.'

  // The form is read only here: the list changes when the staff member asks, not while they choose.
  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const text = (name: string) => { const value = form.get(name); return typeof value === 'string' && value ? value : undefined }
    setFilter({ staffId: text('staffId'), entityType: text('entityType'), from: localInputToInstant(text('from') ?? ''), to: localInputToInstant(text('to') ?? '') })
    setPage(1)
  }

  return (
    <PageShell title="Audit log">
      <form onSubmit={onSubmit} className="flex flex-wrap items-end gap-3 rounded-xl border bg-card p-4">
        {/* Hiding only: the staff list needs staff.manage (the server refuses it otherwise). */}
        {can('staff.manage') ? <StaffFilter /> : null}
        <FormField label="Record type" htmlFor="audit-type">
          <NativeSelect id="audit-type" name="entityType" defaultValue="">
            <option value="">Any</option>
            {recordTypes.map((type) => <option key={type.value} value={type.value}>{type.label}</option>)}
          </NativeSelect>
        </FormField>
        <FormField label="From" htmlFor="audit-from"><Input id="audit-from" name="from" type="datetime-local" /></FormField>
        <FormField label="To" htmlFor="audit-to"><Input id="audit-to" name="to" type="datetime-local" /></FormField>
        <Button type="submit">Apply</Button>
      </form>

      {audit.isError ? <ErrorNote error={audit.error} /> : null}
      <DataTable columns={columns} data={audit.data?.items ?? []} rowId={(item) => item.id} empty={audit.data ? none : audit.isPending ? 'Loading…' : null} />

      {audit.data ? (
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>{total === 1 ? '1 entry' : `${total} entries`}</span>
          <div className="flex items-center gap-2">
            {loading ? <span role="status">Loading…</span> : null}
            <Button size="sm" variant="outline" disabled={loading || page <= 1} onClick={() => setPage(page - 1)}>Previous</Button>
            <span>Page {page} of {pages}</span>
            <Button size="sm" variant="outline" disabled={loading || page >= pages} onClick={() => setPage(page + 1)}>Next</Button>
          </div>
        </div>
      ) : null}
    </PageShell>
  )
}
