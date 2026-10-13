import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, call } from '@/api/client'
import { ApiError } from '@/api/errors'
import type { components } from '@/api/schema'
import { meKey, useSession } from '@/features/auth/session'

export type Staff = components['schemas']['StaffResponse']
type AddStaff = components['schemas']['AddStaffBody']
type UpdateStaff = components['schemas']['UpdateStaffBody']

export const staffKey = ['staff'] as const

export function useStaff() {
  return useQuery({ queryKey: staffKey, queryFn: () => call(() => api.GET('/admin/api/staff')) })
}

/** After any staff write, and after a 409: the list (and the versions in it) must be fresh before the next try. */
function useReload() {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: staffKey })
}

export function useAddStaff() {
  const reload = useReload()
  return useMutation({
    mutationFn: (body: AddStaff) => call(() => api.POST('/admin/api/staff', { body })),
    onSuccess: reload,
  })
}

export function useUpdateStaff() {
  const reload = useReload()
  const queryClient = useQueryClient()
  const { me } = useSession()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateStaff }) =>
      call(() => api.PATCH('/admin/api/staff/{id}', { params: { path: { id } }, body })),
    // A change to yourself also changes the header and what the menu may show.
    onSuccess: (saved) => Promise.all([reload(), saved.id === me.id ? queryClient.invalidateQueries({ queryKey: meKey }) : undefined]),
    onError: (error) => (error instanceof ApiError && error.status === 409 ? reload() : undefined),
  })
}
