import { http, HttpResponse } from 'msw'
import { problem, TRACE } from '@/test/problems'
import { server } from '@/test/server'
import { api, call, onUnauthorized, resetCsrf } from './client'
import { ApiError } from './errors'
import type { components } from './schema'

const role = { id: '0199dd00-0000-7000-8000-0000000000aa', name: 'Front desk', permissions: [], isSystem: false, staffCount: 0, version: 7 }

/** Serves /auth/csrf with token-1, token-2, … and counts the calls. */
function csrfHandler() {
  const state = { calls: 0 }
  server.use(http.get('*/admin/api/auth/csrf', () => HttpResponse.json({ token: `token-${++state.calls}` })))
  return state
}

async function failure(send: () => Promise<unknown>): Promise<ApiError> {
  try {
    await send()
  } catch (error) {
    if (error instanceof ApiError) return error
    throw error
  }
  throw new Error('expected the call to fail')
}

describe('api client', () => {
  beforeEach(() => resetCsrf())

  it('returns the typed body of a successful call', async () => {
    server.use(http.get('*/admin/api/roles', () => HttpResponse.json([role])))
    const roles: components['schemas']['RoleResponse'][] = await call(() => api.GET('/admin/api/roles'))
    expect(roles[0]?.version).toBe(7)
  })

  it('returns undefined for 204', async () => {
    csrfHandler()
    server.use(http.post('*/admin/api/auth/logout', () => new HttpResponse(null, { status: 204 })))
    expect(await call(() => api.POST('/admin/api/auth/logout'))).toBeUndefined()
  })

  it('sends the csrf header on writes and fetches the token once', async () => {
    const csrf = csrfHandler()
    const seen: (string | null)[] = []
    server.use(http.post('*/admin/api/roles', ({ request }) => {
      seen.push(request.headers.get('X-CSRF-TOKEN'))
      return HttpResponse.json(role, { status: 201 })
    }))
    await call(() => api.POST('/admin/api/roles', { body: { name: 'A', permissions: [] } }))
    await call(() => api.POST('/admin/api/roles', { body: { name: 'B', permissions: [] } }))
    expect(seen).toEqual(['token-1', 'token-1'])
    expect(csrf.calls).toBe(1)
  })

  it('needs no csrf token for reads or for the sign-in calls', async () => {
    const csrf = csrfHandler()
    server.use(
      http.get('*/admin/api/roles', () => HttpResponse.json([])),
      http.post('*/admin/api/auth/code', () => new HttpResponse(null, { status: 202 })),
      http.post('*/admin/api/auth/code/verify', () => new HttpResponse(null, { status: 204 })),
    )
    await call(() => api.GET('/admin/api/roles'))
    await call(() => api.POST('/admin/api/auth/code', { body: { email: 'ada@example.test' } }))
    await call(() => api.POST('/admin/api/auth/code/verify', { body: { email: 'ada@example.test', code: '123456' } }))
    expect(csrf.calls).toBe(0)
  })

  it('two writes started together fetch the token once', async () => {
    const csrf = csrfHandler()
    const seen: (string | null)[] = []
    server.use(http.post('*/admin/api/roles', ({ request }) => {
      seen.push(request.headers.get('X-CSRF-TOKEN'))
      return HttpResponse.json(role, { status: 201 })
    }))
    await Promise.all([
      call(() => api.POST('/admin/api/roles', { body: { name: 'A', permissions: [] } })),
      call(() => api.POST('/admin/api/roles', { body: { name: 'B', permissions: [] } })),
    ])
    expect(seen).toEqual(['token-1', 'token-1'])
    expect(csrf.calls).toBe(1)
  })

  it('sends no staff token to a path outside /admin/api', async () => {
    const csrf = csrfHandler()
    const seen: (string | null)[] = []
    server.use(http.post('*/api/v1/auth/logout', ({ request }) => {
      seen.push(request.headers.get('X-CSRF-TOKEN'))
      return new HttpResponse(null, { status: 204 })
    }))
    await call(() => api.POST('/api/v1/auth/logout', { body: { refreshToken: 'r' } }))
    expect(seen).toEqual([null])
    expect(csrf.calls).toBe(0)
  })

  it('a failed token fetch fails the write with its own error and sends nothing', async () => {
    let tokenCalls = 0
    let posts = 0
    server.use(
      http.get('*/admin/api/auth/csrf', () =>
        ++tokenCalls === 1 ? problem(500, 'INTERNAL_ERROR', 'Something went wrong on our side.') : HttpResponse.json({ token: 'token-2' })),
      http.post('*/admin/api/roles', () => {
        posts++
        return HttpResponse.json(role, { status: 201 })
      }),
    )
    const error = await failure(() => call(() => api.POST('/admin/api/roles', { body: { name: 'A', permissions: [] } })))
    expect(error).toMatchObject({ status: 500, code: 'INTERNAL_ERROR', message: 'Something went wrong on our side.', traceId: TRACE })
    expect(tokenCalls).toBe(1)
    expect(posts).toBe(0)

    await call(() => api.POST('/admin/api/roles', { body: { name: 'A', permissions: [] } }))   // the failure was not remembered
    expect(tokenCalls).toBe(2)
    expect(posts).toBe(1)
  })

  it('a 401 on the token fetch fails the write and tells the listeners once', async () => {
    const listener = vi.fn()
    const unsubscribe = onUnauthorized(listener)
    let posts = 0
    server.use(
      http.get('*/admin/api/auth/csrf', () => problem(401, 'UNAUTHENTICATED', 'Sign in again.')),
      http.post('*/admin/api/roles', () => {
        posts++
        return HttpResponse.json(role, { status: 201 })
      }),
    )
    const error = await failure(() => call(() => api.POST('/admin/api/roles', { body: { name: 'A', permissions: [] } })))
    unsubscribe()
    expect(error).toMatchObject({ status: 401, code: 'UNAUTHENTICATED' })
    expect(listener).toHaveBeenCalledTimes(1)
    expect(posts).toBe(0)
  })

  it('retries a write once after CSRF_FAILED', async () => {
    const csrf = csrfHandler()
    const bodies: unknown[] = []
    server.use(http.post('*/admin/api/roles', async ({ request }) => {
      bodies.push(await request.json())
      return request.headers.get('X-CSRF-TOKEN') === 'token-1'
        ? problem(400, 'CSRF_FAILED', 'The security token is missing or out of date. Reload and try again.')
        : HttpResponse.json(role, { status: 201 })
    }))
    const created = await call(() => api.POST('/admin/api/roles', { body: { name: 'A', permissions: [] } }))
    expect(created.id).toBe(role.id)
    expect(csrf.calls).toBe(2)
    expect(bodies).toEqual([{ name: 'A', permissions: [] }, { name: 'A', permissions: [] }])
  })

  it('gives up after the second CSRF_FAILED', async () => {
    csrfHandler()
    let posts = 0
    server.use(http.post('*/admin/api/roles', () => {
      posts++
      return problem(400, 'CSRF_FAILED', 'The security token is missing or out of date. Reload and try again.')
    }))
    const error = await failure(() => call(() => api.POST('/admin/api/roles', { body: { name: 'A', permissions: [] } })))
    expect(error.code).toBe('CSRF_FAILED')
    expect(posts).toBe(2)
  })

  it('a 401 drops the session and the csrf token', async () => {
    const csrf = csrfHandler()
    const listener = vi.fn()
    const unsubscribe = onUnauthorized(listener)
    server.use(http.post('*/admin/api/roles', () => HttpResponse.json(role, { status: 201 })))
    await call(() => api.POST('/admin/api/roles', { body: { name: 'A', permissions: [] } }))

    server.use(http.get('*/admin/api/staff', () => problem(401, 'UNAUTHENTICATED', 'Sign in again.')))
    const error = await failure(() => call(() => api.GET('/admin/api/staff')))
    expect(error.status).toBe(401)
    expect(listener).toHaveBeenCalledTimes(1)

    await call(() => api.POST('/admin/api/roles', { body: { name: 'B', permissions: [] } }))
    expect(csrf.calls).toBe(2)   // the token of the old sign-in was not reused

    unsubscribe()
    await failure(() => call(() => api.GET('/admin/api/staff')))
    expect(listener).toHaveBeenCalledTimes(1)
  })

  it('turns a problem body into an ApiError', async () => {
    server.use(http.get('*/admin/api/staff', () =>
      problem(429, 'RATE_LIMITED', 'Too many requests.', { headers: { 'Retry-After': '42' }, errors: { email: ['Not allowed.'] } })))
    const error = await failure(() => call(() => api.GET('/admin/api/staff')))
    expect(error).toMatchObject({ status: 429, code: 'RATE_LIMITED', message: 'Too many requests.', traceId: TRACE, retryAfterSeconds: 42 })
    expect(error.fieldErrors).toEqual({ email: ['Not allowed.'] })
  })

  it('ignores a Retry-After that is a date', async () => {
    server.use(http.get('*/admin/api/staff', () =>
      problem(503, 'UNAVAILABLE', 'Try again later.', { headers: { 'Retry-After': 'Wed, 21 Oct 2026 07:28:00 GMT' } })))
    const error = await failure(() => call(() => api.GET('/admin/api/staff')))
    expect(error.status).toBe(503)
    expect(error.retryAfterSeconds).toBeUndefined()
  })

  it('copes with an error that is not a problem body', async () => {
    server.use(http.get('*/admin/api/staff', () =>
      new HttpResponse('<html>Bad Gateway</html>', { status: 502, headers: { 'Content-Type': 'text/html', 'X-Trace-Id': TRACE } })))
    const error = await failure(() => call(() => api.GET('/admin/api/staff')))
    expect(error).toMatchObject({ status: 502, code: 'HTTP_502', traceId: TRACE })
    expect(error.fieldErrors).toEqual({})
  })

  it('reports a network failure as NETWORK', async () => {
    server.use(http.get('*/admin/api/staff', () => HttpResponse.error()))
    const error = await failure(() => call(() => api.GET('/admin/api/staff')))
    expect(error).toMatchObject({ status: 0, code: 'NETWORK' })
  })

  it('does not call a bug a network failure', async () => {
    await expect(call(() => Promise.reject(new RangeError('bug')))).rejects.toBeInstanceOf(RangeError)
  })
})
