import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, call } from '@/api/client'
import { ApiError } from '@/api/errors'
import type { components } from '@/api/schema'

export type StorageType = components['schemas']['StorageTypeResponse']
export type CreateStorageType = components['schemas']['CreateStorageTypeBody']
export type UpdateStorageType = components['schemas']['UpdateStorageTypeBody']

export const storageTypesKey = ['storage-types'] as const

export function useStorageTypes(page: number, search: string) {
  return useQuery({
    queryKey: [...storageTypesKey, 'list', page, search],
    queryFn: () => call(() => api.GET('/admin/api/storage-types', { params: { query: { page, search: search || undefined } } })),
    placeholderData: keepPreviousData,
  })
}

// shortcut: one page of 100 covers the handful of types; page the select if they ever pass 100.
export function useStorageTypeOptions() {
  return useQuery({
    queryKey: [...storageTypesKey, 'options'],
    queryFn: () => call(() => api.GET('/admin/api/storage-types', { params: { query: { pageSize: 100 } } })),
  })
}

export function useStorageType(id: string | undefined) {
  return useQuery({
    queryKey: [...storageTypesKey, 'one', id],
    enabled: id !== undefined,
    queryFn: () => call(() => api.GET('/admin/api/storage-types/{id}', { params: { path: { id: id! } } })),
  })
}

/** After any write, and after a 409: the list and the open record must be fresh before the next try. */
export function useStorageTypeWrites() {
  const queryClient = useQueryClient()
  const reload = () => queryClient.invalidateQueries({ queryKey: storageTypesKey })
  const onError = (error: unknown) => (error instanceof ApiError && error.status === 409 ? reload() : undefined)
  return {
    create: useMutation({ mutationFn: (body: CreateStorageType) => call(() => api.POST('/admin/api/storage-types', { body })), onSuccess: reload }),
    update: useMutation({
      mutationFn: ({ id, body }: { id: string; body: UpdateStorageType }) =>
        call(() => api.PATCH('/admin/api/storage-types/{id}', { params: { path: { id } }, body })),
      onSuccess: reload,
      onError,
    }),
    remove: useMutation({
      mutationFn: (row: StorageType) =>
        call(() => api.DELETE('/admin/api/storage-types/{id}', { params: { path: { id: row.id }, query: { version: row.version } } })),
      onSuccess: reload,
      onError,
    }),
  }
}
