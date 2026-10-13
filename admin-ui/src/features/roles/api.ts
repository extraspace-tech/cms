import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, call } from '@/api/client'
import { ApiError } from '@/api/errors'
import type { components } from '@/api/schema'
import { meKey } from '@/features/auth/session'
import { staffKey } from '@/features/staff/api'

export type Role = components['schemas']['RoleResponse']

export const rolesKey = ['roles'] as const

export function useRoles() {
  return useQuery({ queryKey: rolesKey, queryFn: () => call(() => api.GET('/admin/api/roles')) })
}

type CreateRole = components['schemas']['CreateRoleBody']
type UpdateRole = components['schemas']['UpdateRoleBody']

export function usePermissions() {
  return useQuery({ queryKey: ['permissions'], queryFn: () => call(() => api.GET('/admin/api/permissions')), staleTime: Infinity })
}

function useReload() {
  const queryClient = useQueryClient()
  // Staff rows and the header show role names, and the signed-in member's own role may have changed.
  return () => Promise.all([rolesKey, staffKey, meKey].map((queryKey) => queryClient.invalidateQueries({ queryKey })))
}

// 409 = the row changed or is in use, 404 = someone else deleted it: either way the list on screen is old.
const reloadWhenOld = (reload: () => Promise<unknown>) => (error: unknown) =>
  error instanceof ApiError && (error.status === 409 || error.status === 404) ? reload() : undefined

export function useCreateRole() {
  const reload = useReload()
  return useMutation({ mutationFn: (body: CreateRole) => call(() => api.POST('/admin/api/roles', { body })), onSuccess: reload })
}

export function useUpdateRole() {
  const reload = useReload()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateRole }) =>
      call(() => api.PATCH('/admin/api/roles/{id}', { params: { path: { id } }, body })),
    onSuccess: reload,
    onError: reloadWhenOld(reload),
  })
}

export function useDeleteRole() {
  const reload = useReload()
  return useMutation({
    mutationFn: ({ id, version }: { id: string; version: number }) =>
      call(() => api.DELETE('/admin/api/roles/{id}', { params: { path: { id }, query: { version } } })),
    onSuccess: reload,
    onError: reloadWhenOld(reload),
  })
}
