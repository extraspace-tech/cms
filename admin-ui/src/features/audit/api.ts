import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, call } from '@/api/client'
import type { components } from '@/api/schema'

export type AuditItem = components['schemas']['AuditItem']

export type AuditFilter = { staffId?: string; entityType?: string; from?: string; to?: string }

export const PAGE_SIZE = 50

export function useAudit(filter: AuditFilter, page: number) {
  return useQuery({
    queryKey: ['audit', filter, page],
    queryFn: () => call(() => api.GET('/admin/api/audit', { params: { query: { ...filter, page, pageSize: PAGE_SIZE } } })),
    placeholderData: keepPreviousData,
  })
}
