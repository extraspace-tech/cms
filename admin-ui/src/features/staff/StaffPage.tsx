import { useState } from 'react'
import { ErrorNote } from '@/components/ErrorNote'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { columnsFor, DataTable } from '@/components/DataTable'
import { PageShell } from '@/components/PageShell'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useSession } from '@/features/auth/session'
import { useRoles } from '@/features/roles/api'
import { formatInstant, zoneName } from '@/lib/time'
import { useStaff, useUpdateStaff, type Staff } from './api'
import { StaffDialog } from './StaffDialog'

// `active` is the value the staff member asked to set: a reload under the open dialog must not turn the action around.
type Open = { kind: 'add' } | { kind: 'edit'; id: string } | { kind: 'toggle'; id: string; active: boolean } | null

export function StaffPage() {
  const { me } = useSession()
  const staff = useStaff()
  const roles = useRoles()
  const update = useUpdateStaff()
  const [open, setOpen] = useState<Open>(null)

  const systemRoles = new Set((roles.data ?? []).filter((role) => role.isSystem).map((role) => role.id))
  // The dialogs read the row from the list, never a copy: after a 409 the reloaded row carries the new version.
  const selected = open && open.kind !== 'add' ? staff.data?.find((member) => member.id === open.id) : undefined

  // Someone else got there first (seen on the reload after a 409). Not while our own save is still closing the dialog.
  const alreadyDone = open?.kind === 'toggle' && selected?.active === open.active && update.isError

  function close() {
    update.reset()
    setOpen(null)
  }

  const helper = columnsFor<Staff>()
  const columns = helper.columns([
    helper.accessor('email', { header: 'Email' }),
    helper.accessor('displayName', { header: 'Name', cell: (c) => c.getValue() ?? '—' }),
    helper.accessor('roleName', { header: 'Role' }),
    helper.accessor('active', { header: 'Status', cell: (c) => <Badge variant={c.getValue() ? 'default' : 'secondary'}>{c.getValue() ? 'Active' : 'Inactive'}</Badge> }),
    helper.accessor('lastSignInAt', { header: `Last sign-in (${zoneName()})`, cell: (c) => { const at = c.getValue(); return at ? formatInstant(at) : 'Never' } }),
    helper.display({
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      cell: ({ row }) => {
        const member = row.original
        // Hiding only: only an Owner may change an Owner, and nobody deactivates themself (the server refuses both).
        // Until the roles are loaded nobody knows which rows are Owners, so a non-Owner gets no actions yet.
        if (!me.isOwner && (!roles.data || systemRoles.has(member.roleId))) return null
        const toggle = member.active ? 'Deactivate' : 'Reactivate'
        return (
          <div className="flex justify-end gap-2">
            <Button size="sm" variant="outline" aria-label={`Edit ${member.email}`} onClick={() => setOpen({ kind: 'edit', id: member.id })}>Edit</Button>
            {member.id === me.id ? null : (
              <Button size="sm" variant="outline" aria-label={`${toggle} ${member.email}`} onClick={() => setOpen({ kind: 'toggle', id: member.id, active: !member.active })}>
                {toggle}
              </Button>
            )}
          </div>
        )
      },
    }),
  ])

  return (
    <PageShell
      title="Admin Users"
      actions={
        <>
          <Button variant="outline" onClick={() => void staff.refetch()}>Reload</Button>
          <Button onClick={() => setOpen({ kind: 'add' })} disabled={!roles.data}>Add staff</Button>
        </>
      }
    >
      {staff.isError ? <ErrorNote error={staff.error} /> : null}
      {roles.isError ? <ErrorNote error={roles.error} /> : null}
      <DataTable columns={columns} data={staff.data ?? []} rowId={(member) => member.id} empty={staff.data ? 'No staff yet.' : staff.isPending ? 'Loading…' : null} />

      {open?.kind === 'add' && roles.data ? <StaffDialog roles={roles.data} onClose={close} /> : null}
      {open?.kind === 'edit' && selected && roles.data ? <StaffDialog key={selected.id} staff={selected} roles={roles.data} onClose={close} /> : null}
      {open?.kind === 'toggle' && selected ? (
        <ConfirmDialog
          open
          title={open.active ? 'Reactivate staff member' : 'Deactivate staff member'}
          description={alreadyDone
            ? `${selected.email} is already ${open.active ? 'active' : 'inactive'}.`
            : open.active
              ? `${selected.email} will be able to sign in again.`
              : `${selected.email} will be signed out at their next click and cannot sign in again.`}
          confirmLabel={open.active ? 'Reactivate' : 'Deactivate'}
          destructive={!open.active}
          pending={update.isPending}
          error={update.error}
          onConfirm={alreadyDone ? undefined : () => update.mutate({ id: selected.id, body: { active: open.active, version: selected.version } }, { onSuccess: close })}
          onCancel={close}
        />
      ) : null}
    </PageShell>
  )
}
