import { useEffect, useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { ApiError } from '@/api/errors'
import { ErrorNote } from '@/components/ErrorNote'
import { FormField } from '@/components/FormField'
import { NativeSelect } from '@/components/NativeSelect'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { useSession } from '@/features/auth/session'
import type { Role } from '@/features/roles/api'
import { applyServerErrors, conflictMessage, dirtyValues } from '@/lib/forms'
import { useAddStaff, useUpdateStaff, type Staff } from './api'

// The server validates again and is the authority; this only saves a round trip.
const addSchema = z.object({
  email: z.email('Enter a valid email address.').max(254),
  displayName: z.string().max(200, 'At most 200 characters.'),
  roleId: z.string().min(1, 'Choose a role.'),
})
// An edit never sends the email, so a stored address is not checked again here.
const editSchema = addSchema.extend({ email: z.string() })
type Values = z.infer<typeof addSchema>

/** `staff` set = edit that member (always the current row of the list, so `version` is fresh); unset = add. */
export function StaffDialog({ staff, roles, onClose }: { staff?: Staff; roles: Role[]; onClose: () => void }) {
  const { me } = useSession()
  const add = useAddStaff()
  const update = useUpdateStaff()
  const [failure, setFailure] = useState<unknown>(null)
  const editing = staff !== undefined
  const savedName = staff?.displayName ?? ''
  const savedRoleId = staff?.roleId ?? ''
  const form = useForm<Values>({
    resolver: zodResolver(editing ? editSchema : addSchema),
    defaultValues: { email: staff?.email ?? '', displayName: savedName, roleId: savedRoleId },
  })
  const { errors, dirtyFields } = form.formState
  // Hiding only: the server refuses a non-Owner who grants the Owner role (403).
  const options = roles.filter((role) => me.isOwner || !role.isSystem)

  // After a 409 the row is reloaded: show the other person's values in the fields this user did not touch.
  const { reset } = form
  useEffect(() => {
    if (editing) reset((values) => ({ ...values, displayName: savedName, roleId: savedRoleId }), { keepDirtyValues: true })
  }, [editing, reset, savedName, savedRoleId])

  async function onSubmit(values: Values) {
    setFailure(null)
    try {
      if (staff) {
        // Only what this user changed: an untouched field must not undo someone else's save.
        const changed = dirtyValues({ displayName: values.displayName, roleId: values.roleId }, dirtyFields)
        if (Object.keys(changed).length > 0) await update.mutateAsync({ id: staff.id, body: { ...changed, version: staff.version } })
      } else {
        await add.mutateAsync({ email: values.email, displayName: values.displayName, roleId: values.roleId })
      }
      onClose()
    } catch (error) {
      if (!applyServerErrors(error, form.setError, ['email', 'displayName', 'roleId'])) setFailure(error)
    }
  }

  // On an add, CONFLICT means the email is taken: the server's own message says so.
  const stale = editing && failure instanceof ApiError && failure.code === 'CONFLICT'

  return (
    <Dialog open onOpenChange={(next) => { if (!next) onClose() }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{staff ? `Edit ${staff.email}` : 'Add staff'}</DialogTitle>
          <DialogDescription className="sr-only">{staff ? 'Change the display name or the role.' : 'Add a staff member and choose their role.'}</DialogDescription>
        </DialogHeader>
        <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4" noValidate>
          {staff ? null : (
            <FormField label="Email" htmlFor="staff-email" error={errors.email?.message}>
              <Input id="staff-email" type="email" autoComplete="off" {...form.register('email')} />
            </FormField>
          )}
          <FormField label="Display name" htmlFor="staff-name" error={errors.displayName?.message}>
            <Input id="staff-name" autoComplete="off" {...form.register('displayName')} />
          </FormField>
          <FormField label="Role" htmlFor="staff-role" error={errors.roleId?.message}>
            <NativeSelect id="staff-role" {...form.register('roleId')}>
              <option value="">Choose a role</option>
              {options.map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}
            </NativeSelect>
          </FormField>
          {stale ? <p role="alert" className="text-sm text-destructive">{conflictMessage}</p> : null}
          {failure ? <ErrorNote error={failure} /> : null}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>Cancel</Button>
            <Button type="submit" disabled={add.isPending || update.isPending}>{staff ? 'Save' : 'Add'}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
