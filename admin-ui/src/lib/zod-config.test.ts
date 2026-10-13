import { z } from 'zod'

describe('zod config', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('never tries to compile code, which the CSP forbids', () => {
    const blocked = vi.fn(function () {
      throw new EvalError('blocked by the Content-Security-Policy')
    })
    vi.stubGlobal('Function', blocked)

    expect(z.config().jitless).toBe(true)
    expect(z.object({ name: z.string() }).parse({ name: 'Ada' })).toEqual({ name: 'Ada' })
    expect(blocked).not.toHaveBeenCalled()
  })
})
