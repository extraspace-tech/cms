import { z } from 'zod'

// Zod otherwise tries `new Function("")` on the first object parse; our CSP has no 'unsafe-eval', and the browser
// reports even the caught attempt.
z.config({ jitless: true })
