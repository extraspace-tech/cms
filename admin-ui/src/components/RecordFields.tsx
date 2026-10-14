import { useWatch, type FieldValues, type Path, type UseFormReturn } from 'react-hook-form'
import { FormField } from '@/components/FormField'
import { NativeSelect } from '@/components/NativeSelect'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

export type FieldSpec<V extends FieldValues> = {
  name: Path<V>
  label: string
  kind?: 'text' | 'textarea' | 'number' | 'checkbox' | 'select'
  required?: boolean
  options?: { value: string; label: string }[]
  /** Shown, never sent. */
  readOnly?: boolean
  /** A read-only field whose text follows the other fields as they are typed. */
  derive?: (values: V) => string
  help?: string
}

function Derived<V extends FieldValues>({ id, form, derive }: { id: string; form: UseFormReturn<V>; derive: (values: V) => string }) {
  const values = useWatch({ control: form.control }) as V
  return <Input id={id} readOnly value={derive(values)} />
}

const textareaClass = 'w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring'

/** The demo's edit form: every field in its order, required ones marked with *. */
export function RecordFields<V extends FieldValues>({ form, fields }: { form: UseFormReturn<V>; fields: FieldSpec<V>[] }) {
  const errors = form.formState.errors as Record<string, { message?: string } | undefined>
  return (
    <>
      {fields.map((field) => {
        const id = `field-${field.name}`
        if (field.kind === 'checkbox') {
          return (
            <div key={field.name} className="flex items-center gap-2">
              <input id={id} type="checkbox" className="size-4" disabled={field.readOnly} {...form.register(field.name)} />
              <Label htmlFor={id}>{field.label}</Label>
            </div>
          )
        }
        const control = field.derive ? (
          <Derived id={id} form={form} derive={field.derive} />
        ) : field.kind === 'textarea' ? (
          <textarea id={id} rows={4} className={textareaClass} readOnly={field.readOnly} {...form.register(field.name)} />
        ) : field.kind === 'select' ? (
          <NativeSelect id={id} disabled={field.readOnly} {...form.register(field.name)}>
            <option value="">— select —</option>
            {field.options?.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
          </NativeSelect>
        ) : (
          <Input id={id} inputMode={field.kind === 'number' ? 'decimal' : undefined} readOnly={field.readOnly} {...form.register(field.name)} />
        )
        return (
          <div key={field.name} className="space-y-1">
            <FormField label={field.required ? `${field.label} *` : field.label} htmlFor={id} error={errors[field.name]?.message}>{control}</FormField>
            {field.help ? <p className="text-xs text-muted-foreground">{field.help}</p> : null}
          </div>
        )
      })}
    </>
  )
}
