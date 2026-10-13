import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import type { createBrowserRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { ErrorNote } from '@/components/ErrorNote'
import { Button } from '@/components/ui/button'
import { Toaster } from '@/components/ui/sonner'
import { LoginScreen } from '@/features/auth/LoginScreen'
import { SessionProvider, useMe, useSessionWatch } from '@/features/auth/session'

type Router = ReturnType<typeof createBrowserRouter>

function Gate({ router }: { router: Router }) {
  useSessionWatch()
  const me = useMe()

  // Only without an answer: a failed re-check while signed in must not replace the shell.
  if (me.data === undefined)
    return me.isError ? (
      <div className="mx-auto max-w-sm space-y-3 p-6">
        <ErrorNote error={me.error} />
        <Button onClick={() => void me.refetch()}>Try again</Button>
      </div>
    ) : (
      <p className="p-6 text-sm text-muted-foreground">Loading…</p>
    )
  // No /login route: the router keeps its address while the login screen shows, so sign-in returns to the same page.
  if (me.data === null) return <LoginScreen />
  return (
    <SessionProvider me={me.data}>
      <RouterProvider router={router} />
    </SessionProvider>
  )
}

export function App({ router, queryClient }: { router: Router; queryClient: QueryClient }) {
  return (
    <QueryClientProvider client={queryClient}>
      <Gate router={router} />
      <Toaster />
    </QueryClientProvider>
  )
}
