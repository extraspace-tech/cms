import { useEffect, useState } from 'react'
import { ApiError } from '@/api/errors'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { columnsFor, DataTable } from '@/components/DataTable'
import { ErrorNote, toastError } from '@/components/ErrorNote'
import { PageShell } from '@/components/PageShell'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useDeleteRole, useRoles, type Role } from './api'
import { RoleDialog } from './RoleDialog'

// `name` is what the staff member saw when they asked to delete: a reload under the open dialog must not swap it silently.
type Open = { kind: 'add' } | { kind: 'edit'; id: string } | { kind: 'delete'; id: string; name: string } | null

export function RolesPage() {
  const roles = useRoles()
  const remove = useDeleteRole()
  const [open, setOpen] = useState<Open>(null)
  // The dialogs read the row from the list, never a copy: after a 409 the reloaded row carries the new version.
  const selected = open && open.kind !== 'add' ? roles.data?.find((role) => role.id === open.id) : undefined
  const { reset } = remove

  // Someone else deleted the role (seen on a reload): nothing is left to edit or delete.
  const gone = open !== null && open.kind !== 'add' && roles.data !== undefined && !selected
  useEffect(() => {
    if (!gone) return
    reset()
    setOpen(null)
  }, [gone, reset])

  function close() {
    reset()
    setOpen(null)
  }

  const helper = columnsFor<Role>()
  const columns = helper.columns([
    helper.accessor('name', { header: 'Role' }),
    helper.accessor('permissions', {
      header: 'Permissions',
      cell: ({ row }) =>
        row.original.isSystem ? 'Every permission' : (
          <div className="flex flex-wrap gap-1">
            {row.original.permissions.length === 0 ? '—' : row.original.permissions.map((name) => <Badge key={name} variant="secondary">{name}</Badge>)}
          </div>
        ),
    }),
    helper.accessor('staffCount', { header: 'Staff' }),
    helper.display({
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      cell: ({ row }) => {
        const role = row.original
        // Hiding only: system roles cannot be changed or deleted (the server answers 409 SYSTEM_ROLE).
        if (role.isSystem) return null
        return (
          <div className="flex justify-end gap-2">
            <Button size="sm" variant="outline" aria-label={`Edit ${role.name}`} onClick={() => setOpen({ kind: 'edit', id: role.id })}>Edit</Button>
            <Button size="sm" variant="outline" aria-label={`Delete ${role.name}`} onClick={() => setOpen({ kind: 'delete', id: role.id, name: role.name })}>Delete</Button>
          </div>
        )
      },
    }),
  ])

  return (
    <PageShell title="Roles" actions={<Button onClick={() => setOpen({ kind: 'add' })}>Add role</Button>}>
      {roles.isError ? <ErrorNote error={roles.error} /> : null}
      <DataTable columns={columns} data={roles.data ?? []} rowId={(role) => role.id} empty={roles.data ? 'No roles yet.' : roles.isPending ? 'Loading…' : null} />

      {open?.kind === 'add' ? <RoleDialog onClose={close} /> : null}
      {open?.kind === 'edit' && selected ? <RoleDialog key={selected.id} role={selected} onClose={close} /> : null}
      {open?.kind === 'delete' && selected ? (
        <ConfirmDialog
          open
          title="Delete role"
          description={selected.name === open.name
            ? `Delete the role "${open.name}"? This cannot be undone.`
            : `Delete the role "${open.name}"? Someone else has renamed it to "${selected.name}". This cannot be undone.`}
          confirmLabel="Delete"
          destructive
          pending={remove.isPending}
          error={remove.error}
          onConfirm={() => remove.mutate({ id: selected.id, version: selected.version }, {
            onSuccess: close,
            // Already deleted by someone else: the dialog closes with the reload, so the message goes to a toast.
            onError: (error) => { if (error instanceof ApiError && error.status === 404) toastError(error) },
          })}
          onCancel={close}
        />
      ) : null}
    </PageShell>
  )
}
