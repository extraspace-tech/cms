import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, call } from '@/api/client'
import { ApiError } from '@/api/errors'
import type { components } from '@/api/schema'

export type UnitSize = components['schemas']['UnitSizeResponse']
export type CreateUnitSize = components['schemas']['CreateUnitSizeBody']
export type UpdateUnitSize = components['schemas']['UpdateUnitSizeBody']

export const unitSizesKey = ['unit-sizes'] as const

export function useUnitSizes(page: number, search: string) {
  return useQuery({
    queryKey: [...unitSizesKey, 'list', page, search],
    queryFn: () => call(() => api.GET('/admin/api/unit-sizes', { params: { query: { page, search: search || undefined } } })),
    placeholderData: keepPreviousData,
  })
}

export function useUnitSize(id: string | undefined) {
  return useQuery({
    queryKey: [...unitSizesKey, 'one', id],
    enabled: id !== undefined,
    queryFn: () => call(() => api.GET('/admin/api/unit-sizes/{id}', { params: { path: { id: id! } } })),
  })
}

/** After any write, and after a 409: the list and the open record must be fresh before the next try. */
export function useUnitSizeWrites() {
  const queryClient = useQueryClient()
  const reload = () => queryClient.invalidateQueries({ queryKey: unitSizesKey })
  const onError = (error: unknown) => (error instanceof ApiError && error.status === 409 ? reload() : undefined)
  return {
    create: useMutation({ mutationFn: (body: CreateUnitSize) => call(() => api.POST('/admin/api/unit-sizes', { body })), onSuccess: reload }),
    update: useMutation({
      mutationFn: ({ id, body }: { id: string; body: UpdateUnitSize }) =>
        call(() => api.PATCH('/admin/api/unit-sizes/{id}', { params: { path: { id } }, body })),
      onSuccess: reload,
      onError,
    }),
    remove: useMutation({
      mutationFn: (row: UnitSize) =>
        call(() => api.DELETE('/admin/api/unit-sizes/{id}', { params: { path: { id: row.id }, query: { version: row.version } } })),
      onSuccess: reload,
      onError,
    }),
  }
}
