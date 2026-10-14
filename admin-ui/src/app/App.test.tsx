import { act, render, screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { createMemoryRouter, type RouteObject } from 'react-router'
import { makeQueryClient } from '@/lib/query'
import { ownerMe, staffMe } from '@/test/data'
import { problem, TRACE } from '@/test/problems'
import { renderApp } from '@/test/render'
import { server } from '@/test/server'
import { App } from './App'
import { routes, shellRoutes } from './routes'

/** Like renderApp (call it first for the handlers), with the query client and the route list in the test's hands. */
function renderWith(path: string, list: RouteObject[] = routes) {
  const queryClient = makeQueryClient()
  const router = createMemoryRouter(list, { basename: '/admin', initialEntries: [path] })
  render(<App router={router} queryClient={queryClient} />)
  return { queryClient }
}

function sessionRoutes() {
  server.use(
    http.get('*/admin/api/me', () => HttpResponse.json(ownerMe)),
    http.get('*/admin/api/auth/csrf', () => HttpResponse.json({ token: 'csrf-test' })),
  )
}

describe('shell', () => {
  it('shows every menu item to an Owner', async () => {
    renderApp('/admin/')
    const nav = within(await screen.findByRole('navigation'))
    for (const name of ['Dashboard', 'Admin Users', 'Roles', 'Audit Log', 'Jobs'])
      expect(nav.getByRole('link', { name })).toBeInTheDocument()
    expect(nav.getByRole('link', { name: 'Jobs' })).toHaveAttribute('href', '/admin/jobs')
    expect(screen.getByText('Olive Owner')).toBeInTheDocument()
    expect(screen.getByText('Owner')).toBeInTheDocument()
  })

  it('hides menu items the staff member lacks', async () => {
    server.use(http.get('*/admin/api/staff', () => HttpResponse.json([])), http.get('*/admin/api/roles', () => HttpResponse.json([])))
    renderApp('/admin/', { me: staffMe })
    const nav = within(await screen.findByRole('navigation'))
    expect(nav.getByRole('link', { name: 'Admin Users' })).toBeInTheDocument()
    for (const name of ['Roles', 'Audit Log', 'Jobs'])
      expect(nav.queryByRole('link', { name })).not.toBeInTheDocument()
    expect(screen.getByText('desk@example.test')).toBeInTheDocument()   // no display name: the email stands in
  })

  it('groups the sidebar like the demo and hides what the staff member may not use', async () => {
    renderApp('/admin/')
    const nav = within(await screen.findByRole('navigation'))
    expect(nav.getByText('Platform')).toBeInTheDocument()
    expect(nav.getByText('Access Control')).toBeInTheDocument()
    const platform = ['Sites', 'Storage Types', 'Unit Sizes', 'Units']
    const access = ['Admin Users', 'Audit Log', 'Jobs', 'Roles']
    const labels = nav.getAllByRole('link').map((link) => link.textContent)
    expect(labels).toEqual(['Dashboard', ...platform, ...access])
  })

  it('shows only the allowed Access Control items to a non-Owner', async () => {
    renderApp('/admin/', { me: staffMe })   // staff.manage only
    const nav = within(await screen.findByRole('navigation'))
    expect(nav.getByText('Admin Users')).toBeInTheDocument()
    expect(nav.queryByText('Roles')).not.toBeInTheDocument()
    expect(nav.getByText('Sites')).toBeInTheDocument()   // Platform pages are readable by any staff member
  })

  it('refuses a page address the staff member has no permission for', async () => {
    renderApp('/admin/audit', { me: staffMe })
    expect(await screen.findByText('You do not have access to this page.')).toBeInTheDocument()
  })

  it('shows a not-found page for an unknown address', async () => {
    renderApp('/admin/nope')
    expect(await screen.findByText('This page does not exist.')).toBeInTheDocument()
  })

  it('shows the login screen when a later call gets 401, and returns to the same address after sign-in', async () => {
    let signedIn = true
    server.use(
      http.get('*/admin/api/roles', () => HttpResponse.json([])),
      http.get('*/admin/api/staff', () => (signedIn ? HttpResponse.json([]) : problem(401, 'UNAUTHENTICATED', 'Sign in again.'))),
      http.post('*/admin/api/auth/code', () => new HttpResponse(null, { status: 202 })),
      http.post('*/admin/api/auth/code/verify', () => {
        signedIn = true
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { user } = renderApp('/admin/staff')
    expect(await screen.findByRole('heading', { name: 'Admin Users' })).toBeInTheDocument()

    signedIn = false
    await user.click(screen.getByRole('button', { name: 'Reload' }))
    await user.type(await screen.findByLabelText('Email'), 'owner@example.test')
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    await user.type(await screen.findByLabelText('Code'), '123456')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByRole('heading', { name: 'Admin Users' })).toBeInTheDocument()   // not the dashboard
  })

  it('signs out', async () => {
    let csrf: string | null = null
    server.use(http.post('*/admin/api/auth/logout', ({ request }) => {
      csrf = request.headers.get('X-CSRF-TOKEN')
      return new HttpResponse(null, { status: 204 })
    }))
    const { user } = renderApp('/admin/')
    await user.click(await screen.findByRole('button', { name: 'Sign out' }))
    expect(await screen.findByLabelText('Email')).toBeInTheDocument()
    expect(csrf).toBe('csrf-test')
  })

  it('stays signed in and shows the error when the sign-out call fails', async () => {
    server.use(http.post('*/admin/api/auth/logout', () => problem(500, 'INTERNAL_ERROR', 'Something went wrong on our side.')))
    const { user } = renderApp('/admin/')
    await user.click(await screen.findByRole('button', { name: 'Sign out' }))
    expect(await screen.findByText('Something went wrong on our side.')).toBeInTheDocument()
    expect(screen.getByText(new RegExp(TRACE))).toBeInTheDocument()
    expect(screen.getByRole('navigation')).toBeInTheDocument()   // the cookie is still valid: no login screen
    expect(screen.queryByLabelText('Email')).not.toBeInTheDocument()
  })

  it('shows the login screen without an error when sign-out finds the session already ended', async () => {
    server.use(http.post('*/admin/api/auth/logout', () => problem(401, 'UNAUTHENTICATED', 'Sign in again.')))
    const { user } = renderApp('/admin/')
    await user.click(await screen.findByRole('button', { name: 'Sign out' }))
    expect(await screen.findByLabelText('Email')).toBeInTheDocument()
    expect(screen.queryByText('Sign in again.')).not.toBeInTheDocument()
  })

  it('keeps the shell when a later session check fails', async () => {
    sessionRoutes()
    const { queryClient } = renderWith('/admin/')
    expect(await screen.findByRole('navigation')).toBeInTheDocument()

    let asked = 0
    server.use(http.get('*/admin/api/me', () => {
      asked++
      return problem(500, 'INTERNAL_ERROR', 'Something went wrong on our side.')
    }))
    await act(() => queryClient.invalidateQueries({ queryKey: ['me'] }))
    expect(asked).toBe(1)
    expect(screen.getByRole('navigation')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
  })

  it('shows its own message when a page throws', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {})   // React and the router report the throw
    sessionRoutes()
    function Broken(): never {
      throw new Error('boom')
    }
    renderWith('/admin/broken', shellRoutes([{ path: 'broken', Component: Broken }]))
    expect(await screen.findByText('Something went wrong. Reload the page.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
    expect(screen.queryByText('boom')).not.toBeInTheDocument()
    error.mockRestore()
  })

  it('shows a retry and the trace id when the session check itself fails', async () => {
    const { user } = renderApp('/admin/', { me: 'down' })
    expect(await screen.findByText('Something went wrong on our side.')).toBeInTheDocument()
    expect(screen.getByText(new RegExp(TRACE))).toBeInTheDocument()
    server.use(http.get('*/admin/api/me', () => HttpResponse.json(ownerMe)))
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByRole('navigation')).toBeInTheDocument()
  })

  it('switches the theme', async () => {
    const { user } = renderApp('/admin/')
    await user.click(await screen.findByRole('button', { name: 'Dark' }))
    expect(document.documentElement.classList.contains('dark')).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Light' }))
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })
})
