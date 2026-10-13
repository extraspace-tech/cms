import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiError } from '@/api/errors'

export const conflictMessage = 'Someone else changed this record. The list was reloaded; check it and save again.'

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
