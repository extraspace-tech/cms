import { HttpResponse } from 'msw'

export const TRACE = '4bf92f3577b34da6a3ce929d0e0e4736'

/** An error response in the server's shape (S0 spec §4.1). */
export function problem(status: number, code: string, title: string, extra: { errors?: Record<string, string[]>; headers?: Record<string, string> } = {}) {
  return HttpResponse.json(
    { type: 'about:blank', title, status, code, traceId: TRACE, ...(extra.errors ? { errors: extra.errors } : {}) },
    { status, headers: { 'Content-Type': 'application/problem+json', 'X-Trace-Id': TRACE, ...extra.headers } },
  )
}
