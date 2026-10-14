import { useParams } from 'react-router'
import { z } from 'zod'
import { ErrorNote } from '@/components/ErrorNote'
import { RecordEditor } from '@/components/RecordEditor'
import type { FieldSpec } from '@/components/RecordFields'
import { useSession } from '@/features/auth/session'
import { useStorageTypeOptions } from '@/features/storage-types/api'
import { numberText } from '@/lib/forms'
import { atMostTwoDecimals, optionalNumber, wholeNumber } from '@/lib/zod-fields'
import { useUnitSize, useUnitSizeWrites, type CreateUnitSize, type UnitSize, type UpdateUnitSize } from './api'

// Blank sort order on create = 0; an edit cannot clear it (an emptied box would be dropped silently).
function schemaFor(editing: boolean) {
  return z.object({
    key: z.string().trim().min(1, 'Required.').max(50).regex(/^[a-z0-9-]+$/, 'Lower-case letters, digits and - only.'),
    label: z.string().trim().min(1, 'Required.').max(100),
    displayName: z.string().max(100),
    description: z.string().max(1000),
    unitGroup: z.string().max(100),
    imagePath: z.string().max(500),
    minAreaSqft: optionalNumber(0, 99999999).refine(atMostTwoDecimals, 'At most 2 decimals.'),
    maxAreaSqft: optionalNumber(0, 99999999).refine(atMostTwoDecimals, 'At most 2 decimals.'),
    sortOrder: wholeNumber().refine((v) => !editing || v !== '', 'Required.'),
    storageTypeId: z.string(),
  }).refine((v) => v.minAreaSqft === '' || v.maxAreaSqft === '' || Number(v.minAreaSqft) <= Number(v.maxAreaSqft),
    { message: 'Must not be above the max area.', path: ['minAreaSqft'] })
}
type Values = z.infer<ReturnType<typeof schemaFor>>
const schemas = { create: schemaFor(false), edit: schemaFor(true) }

function fieldsFor(editing: boolean, types: { value: string; label: string }[]): FieldSpec<Values>[] {
  return [
    { name: 'key', label: 'Key', required: true },
    { name: 'label', label: 'Label', required: true },
    { name: 'displayName', label: 'Display Name' },
    { name: 'description', label: 'Description', kind: 'textarea' },
    { name: 'unitGroup', label: 'Unit Group' },
    { name: 'imagePath', label: 'Image Path' },
    { name: 'minAreaSqft', label: 'Min Area (sq ft)', kind: 'number' },
    { name: 'maxAreaSqft', label: 'Max Area (sq ft)', kind: 'number', help: 'A unit fits when min ≤ area < max.' },
    { name: 'sortOrder', label: 'Sort Order', kind: 'number', required: editing },
    { name: 'storageTypeId', label: 'Storage Type', kind: 'select', options: types, help: 'Empty = units of any type.' },
  ]
}
const numberFields = ['minAreaSqft', 'maxAreaSqft', 'sortOrder']
const clearable = ['minAreaSqft', 'maxAreaSqft', 'storageTypeId']

// Keys in field order: the PATCH body and its `clear` list follow it.
function values(size?: UnitSize): Values {
  return {
    key: size?.key ?? '', label: size?.label ?? '', displayName: size?.displayName ?? '', description: size?.description ?? '',
    unitGroup: size?.unitGroup ?? '', imagePath: size?.imagePath ?? '', minAreaSqft: numberText(size?.minAreaSqft),
    maxAreaSqft: numberText(size?.maxAreaSqft), sortOrder: numberText(size?.sortOrder), storageTypeId: size?.storageTypeId ?? '',
  }
}

export function UnitSizePage() {
  const { id } = useParams()
  const isNew = id === 'new'
  const size = useUnitSize(isNew ? undefined : id)
  const types = useStorageTypeOptions()
  const { create, update } = useUnitSizeWrites()
  const canWrite = useSession().can('sites.manage')
  // The select needs its options before it mounts, or the record's storage type cannot be shown as selected.
  const failed = (!isNew && size.isError ? size.error : null) ?? (types.isError ? types.error : null)
  if (failed) return <ErrorNote error={failed} />
  if ((!isNew && !size.data) || !types.data) return <p className="text-sm text-muted-foreground">Loading…</p>
  const options = types.data.items.map((t) => ({ value: t.id, label: t.name }))
  return (
    <RecordEditor
      key={id}
      backTo="/unit-sizes" backLabel="Unit Sizes" title={isNew ? 'New Unit Size' : 'Unit Size'} record={size.data}
      schema={isNew ? schemas.create : schemas.edit} defaults={values(size.data)} fields={fieldsFor(!isNew, options)}
      numberFields={numberFields} clearable={clearable} canWrite={canWrite}
      create={(body) => create.mutateAsync(body as CreateUnitSize)}
      update={(recordId, body) => update.mutateAsync({ id: recordId, body: body as UpdateUnitSize })}
    />
  )
}
