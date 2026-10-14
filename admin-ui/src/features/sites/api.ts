import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, call } from '@/api/client'
import { ApiError } from '@/api/errors'
import type { components } from '@/api/schema'

export type SiteListItem = components['schemas']['SiteListItem']
export type Site = components['schemas']['SiteResponse']
export type CreateSite = components['schemas']['CreateSiteBody']
export type UpdateSite = components['schemas']['UpdateSiteBody']

export const sitesKey = ['sites'] as const

export function useSites(page: number, search: string) {
  return useQuery({
    queryKey: [...sitesKey, 'list', page, search],
    queryFn: () => call(() => api.GET('/admin/api/sites', { params: { query: { page, search: search || undefined } } })),
    placeholderData: keepPreviousData,
  })
}

// shortcut: one page of 100 covers the ~24 facilities; page the select if sites ever pass 100.
export function useSiteOptions() {
  return useQuery({ queryKey: [...sitesKey, 'options'], queryFn: () => call(() => api.GET('/admin/api/sites', { params: { query: { pageSize: 100 } } })) })
}

export function useSite(id: string | undefined) {
  return useQuery({
    queryKey: [...sitesKey, 'one', id],
    enabled: id !== undefined,
    queryFn: () => call(() => api.GET('/admin/api/sites/{id}', { params: { path: { id: id! } } })),
  })
}

/** After any write, and after a 409: the list and the open record must be fresh before the next try. */
export function useSiteWrites() {
  const queryClient = useQueryClient()
  const reload = () => queryClient.invalidateQueries({ queryKey: sitesKey })
  const onError = (error: unknown) => (error instanceof ApiError && error.status === 409 ? reload() : undefined)
  return {
    create: useMutation({ mutationFn: (body: CreateSite) => call(() => api.POST('/admin/api/sites', { body })), onSuccess: reload }),
    update: useMutation({
      mutationFn: ({ id, body }: { id: string; body: UpdateSite }) => call(() => api.PATCH('/admin/api/sites/{id}', { params: { path: { id } }, body })),
      onSuccess: reload,
      onError,
    }),
    remove: useMutation({
      mutationFn: (row: SiteListItem) => call(() => api.DELETE('/admin/api/sites/{id}', { params: { path: { id: row.id }, query: { version: row.version } } })),
      onSuccess: reload,
      onError,
    }),
  }
}
