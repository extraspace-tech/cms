import createClient, { type Middleware } from 'openapi-fetch'
import { ApiError } from './errors'
import type { paths } from './schema'

const CSRF_HEADER = 'X-CSRF-TOKEN'
const WRITES = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])
const ADMIN_API = '/admin/api/'
// The emailed code is the credential on these two; the server makes no CSRF check there.
const NO_CSRF = new Set(['/admin/api/auth/code', '/admin/api/auth/code/verify'])

let csrf: Promise<string> | undefined
const unauthorizedListeners = new Set<() => void>()

/** Called on every 401: the cookie expired or the staff member was deactivated. */
export function onUnauthorized(listener: () => void): () => void {
  unauthorizedListeners.add(listener)
  return () => unauthorizedListeners.delete(listener)
}

/** The token belongs to one sign-in: drop it on sign-in, sign-out and 401. */
export function resetCsrf(): void {
  csrf = undefined
}

/** Rejects with the token fetch's own ApiError; a failure is not remembered, so the next write asks again. */
function csrfToken(): Promise<string> {
  const pending = (csrf ??= once(() => api.GET('/admin/api/auth/csrf')).then(({ token }) => token))
  pending.catch(() => {
    if (csrf === pending) csrf = undefined
  })
  return pending
}

const csrfMiddleware: Middleware = {
  async onRequest({ request }) {
    const path = new URL(request.url).pathname
    // Only staff routes take the staff token; the schema also holds the customer paths under /api/v1.
    if (!WRITES.has(request.method) || !path.startsWith(ADMIN_API) || NO_CSRF.has(path)) return request
    request.headers.set(CSRF_HEADER, await csrfToken())   // a failed token fetch throws: the write is never sent bare
    return request
  },
}

export const api = createClient<paths>({
  baseUrl: '/',
  // Looked up per call: openapi-fetch otherwise keeps the fetch that existed at import, which MSW replaces later.
  fetch: (request) => globalThis.fetch(request),
})
api.use(csrfMiddleware)

type Result<T> = { data?: T; error?: unknown; response: Response }

async function once<T>(send: () => Promise<Result<T>>): Promise<T> {
  let result: Result<T>
  try {
    result = await send()
  } catch (error) {
    if (error instanceof ApiError) throw error            // the csrf token fetch failed; it is already in our shape
    if (error instanceof TypeError) throw ApiError.network()   // what fetch throws when the server cannot be reached
    throw error
  }
  if (result.response.ok) return result.data as T   // undefined for 204; the one cast in the UI, on the client's own typed result
  if (result.response.status === 401) {
    resetCsrf()
    for (const listener of unauthorizedListeners) listener()
  }
  throw ApiError.from(result.response, result.error)
}

/**
 * Runs one API call: `await call(() => api.GET('/admin/api/staff'))`. It takes a function so that a write refused
 * with CSRF_FAILED (the token is from an older sign-in) can be sent once more with a new token.
 */
export async function call<T>(send: () => Promise<Result<T>>): Promise<T> {
  try {
    return await once(send)
  } catch (error) {
    if (!(error instanceof ApiError) || error.code !== 'CSRF_FAILED') throw error
    resetCsrf()
    return once(send)
  }
}
