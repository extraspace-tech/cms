import { useEffect, useMemo, useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { ApiError } from '@/api/errors'
import type { components } from '@/api/schema'
import { ErrorNote, toastError } from '@/components/ErrorNote'
import { FormField } from '@/components/FormField'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { applyServerErrors, conflictMessage, dirtyValues } from '@/lib/forms'
import { useCreateRole, usePermissions, useUpdateRole, type Role } from './api'

type Permission = components['schemas']['PermissionResponse']

// The server validates again and is the authority; this only saves a round trip.
const schema = z.object({
  name: z.string().trim().min(1, 'Enter a name.').max(100, 'At most 100 characters.'),
  permissions: z.array(z.string()),
})
type Values = z.infer<typeof schema>

/** `role` set = edit it (always the current row of the list, so `version` is fresh); unset = add. */
export function RoleDialog({ role, onClose }: { role?: Role; onClose: () => void }) {
  const catalogue = usePermissions()
  return (
    <Dialog open onOpenChange={(next) => { if (!next) onClose() }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{role ? `Edit ${role.name}` : 'Add role'}</DialogTitle>
          <DialogDescription className="sr-only">{role ? 'Change the name or the permissions.' : 'Name the role and choose its permissions.'}</DialogDescription>
        </DialogHeader>
        {/* The form starts only with the catalogue: a box that is not there yet cannot show a ticked default. */}
        {catalogue.data ? <RoleForm role={role} catalogue={catalogue.data} onClose={onClose} /> : (
          <>
            {catalogue.isError ? <ErrorNote error={catalogue.error} /> : <p className="text-sm text-muted-foreground">Loading…</p>}
            <DialogFooter>
              <Button type="button" variant="outline" onClick={onClose}>Cancel</Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  )
}

function RoleForm({ role, catalogue, onClose }: { role?: Role; catalogue: Permission[]; onClose: () => void }) {
  const create = useCreateRole()
  const update = useUpdateRole()
  const [failure, setFailure] = useState<unknown>(null)
  const editing = role !== undefined
  const savedName = role?.name ?? ''
  const held = role?.permissions
  // In the catalogue's order, the order the boxes report: the same choice is always the same list.
  const savedPermissions = useMemo(() => catalogue.map((p) => p.name).filter((name) => held?.includes(name) ?? false), [catalogue, held])
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { name: savedName, permissions: savedPermissions },
  })
  const { errors, dirtyFields } = form.formState

  // After a 409 the row is reloaded: show the other person's values in the fields this user did not touch.
  const { reset } = form
  useEffect(() => {
    if (editing) reset((values) => ({ ...values, name: savedName, permissions: savedPermissions }), { keepDirtyValues: true, keepErrors: true })
  }, [editing, reset, savedName, savedPermissions])

  async function onSubmit(values: Values) {
    setFailure(null)
    const permissions = catalogue.map((p) => p.name).filter((name) => values.permissions.includes(name))
    try {
      if (role) {
        // Only what this user changed: an untouched field must not undo someone else's save.
        const changed = dirtyValues({ name: values.name, permissions }, dirtyFields)
        if (Object.keys(changed).length > 0) await update.mutateAsync({ id: role.id, body: { ...changed, version: role.version } })
      } else {
        await create.mutateAsync({ name: values.name, permissions })
      }
      onClose()
    } catch (error) {
      // Someone else deleted the role: the reloaded list has no row and the page closes this dialog, so a toast carries the message.
      if (role && error instanceof ApiError && error.status === 404) toastError(error)
      if (error instanceof ApiError && error.code === 'ROLE_NAME_TAKEN') form.setError('name', { message: error.message })
      else if (!applyServerErrors(error, form.setError, ['name'])) setFailure(error)
    }
  }

  // Only an edit can be stale. A taken name has its own code (ROLE_NAME_TAKEN) and goes on the name field.
  const stale = editing && failure instanceof ApiError && failure.code === 'CONFLICT'
  // The boxes are sent as one list: saving again replaces what the other person saved, so show it.
  const savedNow = stale && dirtyFields.permissions ? (held && held.length > 0 ? held.join(', ') : 'none') : null

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4" noValidate>
      <FormField label="Name" htmlFor="role-name" error={errors.name?.message}>
        <Input id="role-name" autoComplete="off" {...form.register('name')} />
      </FormField>
      <fieldset className="space-y-2">
        <legend className="text-sm font-medium">Permissions</legend>
        {catalogue.map((permission) => (
          <label key={permission.name} className="flex items-start gap-2 text-sm">
            <input type="checkbox" className="mt-1" value={permission.name} {...form.register('permissions')} />
            <span>
              <span className="font-mono text-xs">{permission.name}</span>
              <span className="block text-muted-foreground">{permission.description}</span>
            </span>
          </label>
        ))}
      </fieldset>
      {stale ? <p role="alert" className="text-sm text-destructive">{conflictMessage}</p> : null}
      {savedNow ? <p className="text-sm">Saved permissions now: {savedNow}. Saving again replaces them with the boxes ticked above.</p> : null}
      {failure ? <ErrorNote error={failure} /> : null}
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onClose}>Cancel</Button>
        <Button type="submit" disabled={create.isPending || update.isPending}>{role ? 'Save' : 'Add'}</Button>
      </DialogFooter>
    </form>
  )
}
