import { useEffect, useRef } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { api, call } from '@/api/client'
import type { components } from '@/api/schema'

export type UnitListItem = components['schemas']['UnitListItem']
export type UnitSyncStatus = components['schemas']['UnitSyncStatus']

export const unitsKey = ['units'] as const
export const unitSyncKey = ['unit-sync'] as const

/** `available`: '' = any, else 'true' / 'false' as in the address. */
export function useUnits(page: number, search: string, siteId: string, available: string) {
  return useQuery({
    queryKey: [...unitsKey, 'list', page, search, siteId, available],
    queryFn: () => call(() => api.GET('/admin/api/units', { params: { query: {
      page, search: search || undefined, siteId: siteId || undefined, available: available === '' ? undefined : available === 'true',
    } } })),
    placeholderData: keepPreviousData,
  })
}

export function useUnit(id: string) {
  return useQuery({ queryKey: [...unitsKey, 'one', id], queryFn: () => call(() => api.GET('/admin/api/units/{id}', { params: { path: { id } } })) })
}

/** Re-read every 10 s while the page is open: a sync runs in the background and ends on its own. When one ends, the unit lists reload. */
export function useUnitSync() {
  const queryClient = useQueryClient()
  const query = useQuery({ queryKey: unitSyncKey, queryFn: () => call(() => api.GET('/admin/api/unit-sync')), refetchInterval: 10_000 })
  const seen = useRef<Map<string, string | null> | null>(null)
  useEffect(() => {
    if (!query.data) return
    const before = seen.current
    seen.current = new Map(query.data.map((s) => [s.siteId, s.lastSyncedAt]))
    if (before && query.data.some((s) => before.has(s.siteId) && before.get(s.siteId) !== s.lastSyncedAt))
      void queryClient.invalidateQueries({ queryKey: [...unitsKey, 'list'] })
  }, [query.data, queryClient])
  return query
}

export function useStartUnitSync() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ siteId, full }: { siteId: string; full: boolean }) =>
      call(() => api.POST('/admin/api/sites/{id}/unit-sync', { params: { path: { id: siteId } }, body: { full } })),
    onSuccess: () => {
      toast.success('Sync started. The table updates when it finishes.')
      return queryClient.invalidateQueries({ queryKey: unitSyncKey })
    },
  })
}
