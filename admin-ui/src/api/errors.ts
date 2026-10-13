import type { components } from './schema'

export type Problem = components['schemas']['Problem']

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

/** Every failed call becomes one of these. `message` is the server's title, written for people. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly traceId: string | undefined
  readonly fieldErrors: Record<string, string[]>
  readonly retryAfterSeconds: number | undefined

  private constructor(status: number, code: string, title: string, traceId?: string, fieldErrors: Record<string, string[]> = {}, retryAfterSeconds?: number) {
    super(title)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.traceId = traceId
    this.fieldErrors = fieldErrors
    this.retryAfterSeconds = retryAfterSeconds
  }

  static network(): ApiError {
    return new ApiError(0, 'NETWORK', 'Cannot reach the server. Check your connection and try again.')
  }

  /** `body` is what openapi-fetch parsed: the problem object, or text when a proxy answered instead of the app. */
  static from(response: Response, body: unknown): ApiError {
    const problem = isRecord(body) ? body : {}
    const retryAfter = Number(response.headers.get('Retry-After'))
    const fieldErrors: Record<string, string[]> = {}
    if (isRecord(problem.errors))
      for (const [field, messages] of Object.entries(problem.errors))
        if (Array.isArray(messages)) fieldErrors[field] = messages.filter((m): m is string => typeof m === 'string')
    return new ApiError(
      response.status,
      typeof problem.code === 'string' ? problem.code : `HTTP_${response.status}`,
      typeof problem.title === 'string' ? problem.title : 'Something went wrong. Please try again.',
      typeof problem.traceId === 'string' ? problem.traceId : (response.headers.get('X-Trace-Id') ?? undefined),
      fieldErrors,
      Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : undefined,
    )
  }
}
