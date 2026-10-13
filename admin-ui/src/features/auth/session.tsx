import { createContext, useContext, useEffect, type ReactNode } from 'react'
import { useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, call, onUnauthorized, resetCsrf } from '@/api/client'
import { ApiError } from '@/api/errors'
import type { components } from '@/api/schema'

export type Me = components['schemas']['AdminMeResponse']

export const meKey = ['me'] as const

// null = not signed in. For this one call a 401 is an answer, not an error.
async function fetchMe(): Promise<Me | null> {
  try {
    return await call(() => api.GET('/admin/api/me'))
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return null
    throw error
  }
}

export function useMe() {
  return useQuery({ queryKey: meKey, queryFn: fetchMe, staleTime: Infinity })
}

/** Forgets the staff member and everything loaded for them. */
function forget(queryClient: QueryClient) {
  resetCsrf()
  queryClient.setQueryData(meKey, null)
  queryClient.removeQueries({ predicate: (query) => query.queryKey[0] !== meKey[0] })
}

/** Any 401, from any call, means the sign-in ended: show the login screen. */
export function useSessionWatch() {
  const queryClient = useQueryClient()
  useEffect(() => onUnauthorized(() => forget(queryClient)), [queryClient])
}

/** Asks the server who is signed in now. False = the code was accepted but there is still no session. */
export function useSignedIn() {
  const queryClient = useQueryClient()
  return async (): Promise<boolean> => {
    resetCsrf()   // a token fetched before sign-in belongs to nobody
    const me = await queryClient.fetchQuery({ queryKey: meKey, queryFn: fetchMe, staleTime: 0 })
    return me !== null
  }
}

export function useSignOut() {
  const queryClient = useQueryClient()
  return async () => {
    try {
      await call(() => api.POST('/admin/api/auth/logout'))
    } catch (error) {
      // 401 = already signed out, and the unauthorized listener has shown the login screen.
      if (error instanceof ApiError && error.status === 401) return
      throw error   // the cookie is still valid: stay in the shell, or a reload would sign the same person back in
    }
    forget(queryClient)
  }
}

type Session = { me: Me; can: (permission: string) => boolean }

const SessionContext = createContext<Session | null>(null)

export function SessionProvider({ me, children }: { me: Me; children: ReactNode }) {
  // Hiding only: the server checks every call (S0 spec §6.3).
  const session: Session = { me, can: (permission) => me.permissions.includes(permission) }
  return <SessionContext value={session}>{children}</SessionContext>
}

export function useSession(): Session {
  const session = useContext(SessionContext)
  if (!session) throw new Error('useSession is only available inside the signed-in shell.')
  return session
}
