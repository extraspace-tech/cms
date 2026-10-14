import { useEffect, useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm, type DefaultValues, type FieldValues } from 'react-hook-form'
import { useNavigate } from 'react-router'
import type { z } from 'zod'
import { ApiError } from '@/api/errors'
import { ErrorNote } from '@/components/ErrorNote'
import { RecordFields, type FieldSpec } from '@/components/RecordFields'
import { RecordPage } from '@/components/RecordPage'
import { Button } from '@/components/ui/button'
import { applyServerErrors, createBody, dirtyValues, patchBody, recordReloadedNote } from '@/lib/forms'

type Props<R extends { id: string; version: number }, V extends FieldValues> = {
  backTo: string
  backLabel: string
  title: string
  /** Unset = a new record. Always the freshest copy: after a 409 the page reloads it. */
  record?: R
  schema: z.ZodType<V, V>
  /** In field order: the PATCH body and its `clear` list follow this order. */
  defaults: V
  fields: FieldSpec<V>[]
  numberFields?: readonly string[]
  clearable?: readonly string[]
  canWrite: boolean
  create: (body: Record<string, unknown>) => Promise<unknown>
  update: (id: string, body: Record<string, unknown>) => Promise<unknown>
}

/** A record page with its form: POST for a new record, PATCH of only the changed fields + version for an edit (S1 spec §11.1). */
export function RecordEditor<R extends { id: string; version: number }, V extends FieldValues>(props: Props<R, V>) {
  const { record, fields, numberFields = [], clearable = [] } = props
  const navigate = useNavigate()
  const [failure, setFailure] = useState<unknown>(null)
  const [saving, setSaving] = useState(false)
  const form = useForm<V>({ resolver: zodResolver(props.schema), defaultValues: props.defaults as DefaultValues<V> })
  // A derived field only shows a value the server sets: never sent, like a read-only one.
  const readOnlyNames = new Set<string>(fields.filter((f) => f.readOnly || f.derive).map((f) => f.name))

  // After a 409 the record is reloaded: show the other person's values in the fields this user did not touch.
  const { reset } = form
  const defaultsKey = JSON.stringify(props.defaults)
  useEffect(() => {
    reset(JSON.parse(defaultsKey) as V, { keepDirtyValues: true })
  }, [reset, defaultsKey])

  async function onSubmit(values: V) {
    setFailure(null)
    setSaving(true)
    try {
      const names = Object.keys(props.defaults).filter((name) => !readOnlyNames.has(name))
      const sendable: Record<string, unknown> = Object.fromEntries(names.map((name) => [name, values[name]]))
      if (record) {
        const changed = dirtyValues(sendable, form.formState.dirtyFields as Partial<Record<string, unknown>>)
        if (Object.keys(changed).length > 0) await props.update(record.id, { ...patchBody(changed, numberFields, clearable), version: record.version })
      } else {
        await props.create(createBody(sendable, numberFields))
      }
      navigate(props.backTo)
    } catch (error) {
      if (!applyServerErrors(error, form.setError, fields.map((f) => f.name))) setFailure(error)
    } finally {
      setSaving(false)
    }
  }

  // CONFLICT covers a stale version, a duplicate value and a record in use: the server's title says which.
  const reloaded = record !== undefined && failure instanceof ApiError && failure.code === 'CONFLICT'
  const readOnly = !props.canWrite
  return (
    <RecordPage backTo={props.backTo} backLabel={props.backLabel} title={props.title} readOnly={readOnly}>
      <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4" noValidate>
        <fieldset disabled={readOnly} className="space-y-4">
          <RecordFields form={form} fields={fields} />
        </fieldset>
        {failure ? <ErrorNote error={failure} /> : null}
        {reloaded ? <p className="text-sm text-muted-foreground">{recordReloadedNote}</p> : null}
        {readOnly ? null : (
          <div className="flex gap-2">
            <Button type="submit" disabled={saving}>{record ? 'Save changes' : 'Create'}</Button>
            <Button type="button" variant="outline" onClick={() => navigate(props.backTo)}>Cancel</Button>
          </div>
        )}
      </form>
    </RecordPage>
  )
}
