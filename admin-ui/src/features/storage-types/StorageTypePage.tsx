import { useParams } from 'react-router'
import { z } from 'zod'
import { ErrorNote } from '@/components/ErrorNote'
import { RecordEditor } from '@/components/RecordEditor'
import type { FieldSpec } from '@/components/RecordFields'
import { useSession } from '@/features/auth/session'
import { numberText } from '@/lib/forms'
import { wholeNumber } from '@/lib/zod-fields'
import { useStorageType, useStorageTypeWrites, type CreateStorageType, type StorageType, type UpdateStorageType } from './api'

// Blank on create = 0; an edit cannot clear it (an emptied box would be dropped silently).
function schemaFor(editing: boolean) {
  return z.object({
    key: z.string().trim().min(1, 'Required.').max(50).regex(/^[a-z0-9-]+$/, 'Lower-case letters, digits and - only.'),
    name: z.string().trim().min(1, 'Required.').max(100),
    displayName: z.string().max(100),
    iconPath: z.string().max(500),
    imagePath: z.string().max(500),
    sortOrder: wholeNumber().refine((v) => !editing || v !== '', 'Required.'),
    active: z.boolean(),
  })
}
type Values = z.infer<ReturnType<typeof schemaFor>>

function fieldsFor(editing: boolean): FieldSpec<Values>[] {
  return [
    // The key cannot change once saved: the unit sync finds the two seeded types by it.
    { name: 'key', label: 'Key', required: true, readOnly: editing },
    { name: 'name', label: 'Name', required: true },
    { name: 'displayName', label: 'Display Name' },
    { name: 'iconPath', label: 'Icon Path' },
    { name: 'imagePath', label: 'Image Path' },
    { name: 'sortOrder', label: 'Sort Order', kind: 'number', required: editing },
    { name: 'active', label: 'Active', kind: 'checkbox' },
  ]
}
const forms = { create: { schema: schemaFor(false), fields: fieldsFor(false) }, edit: { schema: schemaFor(true), fields: fieldsFor(true) } }
const numberFields = ['sortOrder']

function values(type?: StorageType): Values {
  return {
    key: type?.key ?? '', name: type?.name ?? '', displayName: type?.displayName ?? '', iconPath: type?.iconPath ?? '',
    imagePath: type?.imagePath ?? '', sortOrder: numberText(type?.sortOrder), active: type?.active ?? true,
  }
}

export function StorageTypePage() {
  const { id } = useParams()
  const isNew = id === 'new'
  const type = useStorageType(isNew ? undefined : id)
  const { create, update } = useStorageTypeWrites()
  const canWrite = useSession().can('sites.manage')
  const form = isNew ? forms.create : forms.edit
  if (!isNew && !type.data) return type.isError ? <ErrorNote error={type.error} /> : <p className="text-sm text-muted-foreground">Loading…</p>
  return (
    <RecordEditor
      key={id}
      backTo="/storage-types" backLabel="Storage Types" title={isNew ? 'New Storage Type' : 'Storage Type'} record={type.data}
      schema={form.schema} defaults={values(type.data)} fields={form.fields} numberFields={numberFields} canWrite={canWrite}
      create={(body) => create.mutateAsync(body as CreateStorageType)}
      update={(recordId, body) => update.mutateAsync({ id: recordId, body: body as UpdateStorageType })}
    />
  )
}
