import { render } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { createMemoryRouter } from 'react-router'
import type { components } from '@/api/schema'
import { resetCsrf } from '@/api/client'
import { App } from '@/app/App'
import { routes } from '@/app/routes'
import { makeQueryClient } from '@/lib/query'
import { ownerMe } from './data'
import { problem } from './problems'
import { server } from './server'

type Me = components['schemas']['AdminMeResponse']

/** Renders the whole app at an address. `me: null` = not signed in; `'down'` = the session check fails with 500. */
export function renderApp(path: string, options: { me?: Me | null | 'down' } = {}) {
  const me = options.me === undefined ? ownerMe : options.me
  resetCsrf()
  server.use(
    http.get('*/admin/api/me', () =>
      me === 'down' ? problem(500, 'INTERNAL_ERROR', 'Something went wrong on our side.')
      : me ? HttpResponse.json(me) : problem(401, 'UNAUTHENTICATED', 'Sign in again.')),
    http.get('*/admin/api/auth/csrf', () => HttpResponse.json({ token: 'csrf-test' })),
  )
  const router = createMemoryRouter(routes, { basename: '/admin', initialEntries: [path] })
  const user = userEvent.setup()
  return { user, router, ...render(<App router={router} queryClient={makeQueryClient()} />) }
}
