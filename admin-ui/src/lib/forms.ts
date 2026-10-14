import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiError } from '@/api/errors'

export const conflictMessage = 'Someone else changed this record. The list was reloaded; check it and save again.'

/** A record page after any 409 (stale version, duplicate value, record in use): the page reloads the record, typed values stay. */
export const recordReloadedNote = 'The record was reloaded; your changes are kept.'

/** Puts the server's field errors (keys are camelCase field names) on the form. False = nothing placed, show the error as a whole. */
export function applyServerErrors<T extends FieldValues>(error: unknown, setError: UseFormSetError<T>, fields: readonly Path<T>[]): boolean {
  if (!(error instanceof ApiError)) return false
  let placed = false
  for (const field of fields) {
    const message = error.fieldErrors[field]?.[0]
    if (message) {
      setError(field, { message })
      placed = true
    }
  }
  return placed
}

/** Only the fields the user changed (`formState.dirtyFields`): a PATCH that leaves a field out leaves it alone on the server. */
export function dirtyValues<T extends FieldValues>(values: T, dirtyFields: Partial<Record<keyof T, unknown>>): Partial<T> {
  const changed: Partial<T> = {}
  for (const field in values) if (dirtyFields[field]) changed[field] = values[field]
  return changed
}

/** A stored number as form text ('' for none). */
export function numberText(value: number | null | undefined): string {
  return value === null || value === undefined ? '' : String(value)
}

/**
 * A PATCH body from the changed fields (S1-1 decision 12): text as typed ('' clears it on the server), numbers converted, an
 * emptied clearable number or link named in `clear`. An emptied number that cannot be cleared is left unchanged.
 */
export function patchBody(changed: Record<string, unknown>, numberFields: readonly string[], clearable: readonly string[]) {
  const body: Record<string, unknown> = {}
  const clear: string[] = []
  for (const [name, value] of Object.entries(changed)) {
    if (value === '' && clearable.includes(name)) clear.push(name)
    else if (numberFields.includes(name)) { if (value !== '') body[name] = Number(value) }
    else body[name] = value
  }
  if (clear.length > 0) body.clear = clear
  return body
}

/** A POST body: empty text left out (the server applies its defaults), numbers converted. */
export function createBody(values: Record<string, unknown>, numberFields: readonly string[]) {
  const body: Record<string, unknown> = {}
  for (const [name, value] of Object.entries(values)) {
    if (value === '') continue
    body[name] = numberFields.includes(name) ? Number(value) : value
  }
  return body
}
