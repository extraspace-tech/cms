import { toast } from 'sonner'
import { ApiError } from '@/api/errors'

function describe(error: unknown): { message: string; traceId?: string } {
  if (error instanceof ApiError) return { message: error.message, traceId: error.traceId }
  return { message: 'Something went wrong. Please try again.' }
}

function copy(traceId: string) {
  void navigator.clipboard?.writeText(traceId)
}

/** An error where it happened, with the trace id support needs to find the request (S0 spec §4.4). */
export function ErrorNote({ error }: { error: unknown }) {
  const { message, traceId } = describe(error)
  return (
    <div role="alert" className="rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
      <p>{message}</p>
      {traceId ? (
        <p className="mt-1 flex items-center gap-2 text-xs opacity-80">
          <span>Trace {traceId}</span>
          <button type="button" className="underline" onClick={() => copy(traceId)}>Copy</button>
        </p>
      ) : null}
    </div>
  )
}

export function toastError(error: unknown): void {
  const { message, traceId } = describe(error)
  toast.error(message, traceId
    ? { description: `Trace ${traceId}`, action: { label: 'Copy', onClick: () => copy(traceId) } }
    : undefined)
}
