import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { ownerMe } from '@/test/data'
import { problem, TRACE } from '@/test/problems'
import { renderApp } from '@/test/render'
import { server } from '@/test/server'

function codeRoutes() {
  const sent: unknown[] = []
  server.use(http.post('*/admin/api/auth/code', async ({ request }) => {
    sent.push(await request.json())
    return new HttpResponse(null, { status: 202 })
  }))
  return sent
}

describe('sign-in', () => {
  it('signs in with an email and a code, and lands on the address that was asked for', async () => {
    const sent = codeRoutes()
    const { user } = renderApp('/admin/staff', { me: null })
    await user.type(await screen.findByLabelText('Email'), 'Owner@Example.test')
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    await waitFor(() => expect(sent).toEqual([{ email: 'owner@example.test' }]))

    let verified: unknown
    server.use(
      http.post('*/admin/api/auth/code/verify', async ({ request }) => {
        verified = await request.json()
        server.use(http.get('*/admin/api/me', () => HttpResponse.json(ownerMe)))   // the cookie is set from here on
        return new HttpResponse(null, { status: 204 })
      }),
      http.get('*/admin/api/staff', () => HttpResponse.json([])),
      http.get('*/admin/api/roles', () => HttpResponse.json([])),
    )
    await user.type(await screen.findByLabelText('Code'), '123456')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('heading', { name: 'Staff' })).toBeInTheDocument()
    expect(verified).toEqual({ email: 'owner@example.test', code: '123456' })
    expect(screen.getByText('Olive Owner')).toBeInTheDocument()
  })

  it('says the same thing whether or not the address is known', async () => {
    codeRoutes()
    const { user } = renderApp('/admin/', { me: null })
    await user.type(await screen.findByLabelText('Email'), 'nobody@example.test')
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    expect(await screen.findByText(/If nobody@example.test belongs to a staff member/)).toBeInTheDocument()
  })

  it('shows a wrong code with its trace id and lets the user try again', async () => {
    codeRoutes()
    server.use(http.post('*/admin/api/auth/code/verify', () => problem(400, 'INVALID_CODE', 'The code is wrong or has expired')))
    const { user } = renderApp('/admin/', { me: null })
    await user.type(await screen.findByLabelText('Email'), 'owner@example.test')
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    await user.type(await screen.findByLabelText('Code'), '000000')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByText('The code is wrong or has expired')).toBeInTheDocument()
    expect(screen.getByText(new RegExp(TRACE))).toBeInTheDocument()
    expect(screen.getByLabelText('Code')).toBeInTheDocument()
  })

  it('goes back to the email step after too many attempts', async () => {
    codeRoutes()
    server.use(http.post('*/admin/api/auth/code/verify', () => problem(400, 'TOO_MANY_ATTEMPTS', 'Too many wrong codes. Request a new one.')))
    const { user } = renderApp('/admin/', { me: null })
    await user.type(await screen.findByLabelText('Email'), 'owner@example.test')
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    await user.type(await screen.findByLabelText('Code'), '000000')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByText('Too many wrong codes. Request a new one.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Send code' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Code')).not.toBeInTheDocument()
  })

  it.each([
    ['1', 'Try again in 1 second.'],
    ['30', 'Try again in 30 seconds.'],
    ['60', 'Try again in 1 minute.'],
    ['95', 'Try again in 2 minutes.'],
  ])('shows how long to wait on 429 (Retry-After %s)', async (retryAfter, text) => {
    server.use(http.post('*/admin/api/auth/code', () => problem(429, 'RATE_LIMITED', 'Too many requests.', { headers: { 'Retry-After': retryAfter } })))
    const { user } = renderApp('/admin/', { me: null })
    await user.type(await screen.findByLabelText('Email'), 'owner@example.test')
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    expect(await screen.findByText(text)).toBeInTheDocument()
  })

  it('goes back to the email step with a message when the code is accepted but there is no session', async () => {
    codeRoutes()
    server.use(http.post('*/admin/api/auth/code/verify', () => new HttpResponse(null, { status: 204 })))   // /me keeps answering 401
    const { user } = renderApp('/admin/', { me: null })
    await user.type(await screen.findByLabelText('Email'), 'owner@example.test')
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    await user.type(await screen.findByLabelText('Code'), '123456')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByText('Sign-in did not complete. Request a new code.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Send code' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Code')).not.toBeInTheDocument()
  })

  it('keeps only the digits of a pasted code', async () => {
    codeRoutes()
    let verified: unknown
    server.use(http.post('*/admin/api/auth/code/verify', async ({ request }) => {
      verified = await request.json()
      return problem(400, 'INVALID_CODE', 'The code is wrong or has expired')
    }))
    const { user } = renderApp('/admin/', { me: null })
    await user.type(await screen.findByLabelText('Email'), 'owner@example.test')
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    await user.click(await screen.findByLabelText('Code'))
    await user.paste(' 123 456 789')
    expect(screen.getByLabelText('Code')).toHaveValue('123456')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    await waitFor(() => expect(verified).toEqual({ email: 'owner@example.test', code: '123456' }))
  })

  it('clears the wait notice when the user picks another email', async () => {
    codeRoutes()
    server.use(http.post('*/admin/api/auth/code/verify', () => problem(429, 'RATE_LIMITED', 'Too many requests.', { headers: { 'Retry-After': '30' } })))
    const { user } = renderApp('/admin/', { me: null })
    await user.type(await screen.findByLabelText('Email'), 'owner@example.test')
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    await user.type(await screen.findByLabelText('Code'), '123456')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByText('Try again in 30 seconds.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Use another email' }))
    expect(screen.getByLabelText('Email')).toBeInTheDocument()
    expect(screen.queryByText(/Try again in/)).not.toBeInTheDocument()
    expect(screen.queryByText('Too many requests.')).not.toBeInTheDocument()
  })

  it('does not allow a new code for 60 seconds', async () => {
    codeRoutes()
    const { user } = renderApp('/admin/', { me: null })
    await user.type(await screen.findByLabelText('Email'), 'owner@example.test')
    await user.click(screen.getByRole('button', { name: 'Send code' }))
    const resend = await screen.findByRole('button', { name: /Send a new code/ })
    expect(resend).toBeDisabled()
    await waitFor(() => expect(resend).toHaveTextContent(/in \d+ s/))
  })
})
