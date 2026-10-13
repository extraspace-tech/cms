import './relative-fetch'
import '@/lib/zod-config'
import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { toast } from 'sonner'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { server } from './server'

// jsdom has no matchMedia (sonner and our theme call it)
if (!window.matchMedia) {
  window.matchMedia = (query: string) =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    }) as MediaQueryList
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

// The client turns a refused request into NETWORK or "no token", so a test with a missing handler could still pass.
const unhandled: string[] = []

beforeAll(() =>
  server.listen({
    onUnhandledFrame({ frame, defaults }) {
      const request = isRecord(frame.data) ? frame.data.request : undefined
      unhandled.push(isRecord(request) ? `${String(request.method)} ${String(request.url)}` : frame.protocol)
      defaults.error()
    },
  }),
)
afterEach(() => {
  server.resetHandlers()
  cleanup()
  toast.dismiss()   // sonner keeps its toasts in a module: the next test's Toaster would show them again
  const missed = unhandled.splice(0)
  if (missed.length > 0) throw new Error(`No MSW handler for: ${missed.join(', ')}`)
})
afterAll(() => server.close())
