import { useEffect, useState, type FormEvent } from 'react'
import { api, call } from '@/api/client'
import { ApiError } from '@/api/errors'
import { ErrorNote } from '@/components/ErrorNote'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useSignedIn } from './session'

const RESEND_SECONDS = 60   // the server refuses a second send sooner (S0 spec §6.1)

function wait(seconds: number): string {
  const [count, unit] = seconds < 60 ? [Math.ceil(seconds), 'second'] : [Math.ceil(seconds / 60), 'minute']
  return `${count} ${unit}${count === 1 ? '' : 's'}`
}

export function LoginScreen() {
  const signedIn = useSignedIn()
  const [step, setStep] = useState<'email' | 'code'>('email')
  const [email, setEmail] = useState('')
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)   // a string is our own message; anything else came from a call
  const [notice, setNotice] = useState<string | null>(null)
  const [resendIn, setResendIn] = useState(0)

  useEffect(() => {
    if (resendIn <= 0) return
    const timer = setTimeout(() => setResendIn((s) => s - 1), 1000)
    return () => clearTimeout(timer)
  }, [resendIn])

  async function run(action: () => Promise<void>) {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await action()
    } catch (caught) {
      setError(caught)
      if (caught instanceof ApiError && caught.retryAfterSeconds) setNotice(`Try again in ${wait(caught.retryAfterSeconds)}.`)
      if (caught instanceof ApiError && caught.code === 'TOO_MANY_ATTEMPTS') {
        setStep('email')
        setCode('')
      }
    } finally {
      setBusy(false)
    }
  }

  const address = email.trim().toLowerCase()

  const sendCode = () => run(async () => {
    await call(() => api.POST('/admin/api/auth/code', { body: { email: address } }))
    setStep('code')
    setResendIn(RESEND_SECONDS)
  })

  const verify = () => run(async () => {
    await call(() => api.POST('/admin/api/auth/code/verify', { body: { email: address, code } }))
    if (await signedIn()) return
    // The code is used up, but there is no session (cookie refused, or the staff member was just deactivated).
    setError('Sign-in did not complete. Request a new code.')
    setStep('email')
    setCode('')
  })

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    void (step === 'email' ? sendCode() : verify())
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-muted/40 px-4">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center gap-3 text-center">
          <div className="flex size-11 items-center justify-center rounded-xl bg-primary text-lg font-bold text-primary-foreground">e</div>
          <div>
            <h1 className="text-lg font-bold">ExtraSpace Admin</h1>
            <p className="text-sm text-muted-foreground">Sign in with your work email</p>
          </div>
        </div>

        <form onSubmit={onSubmit} className="space-y-4 rounded-xl border bg-card p-6 shadow-sm">
          {step === 'email' ? (
            <div className="space-y-1.5">
              <Label htmlFor="email">Email</Label>
              <Input id="email" type="email" autoComplete="username" required autoFocus value={email} onChange={(e) => setEmail(e.target.value)} />
            </div>
          ) : (
            <>
              <p className="text-sm text-muted-foreground">
                If {address} belongs to a staff member, a 6-digit code is on its way. It is valid for 10 minutes.
              </p>
              <div className="space-y-1.5">
                <Label htmlFor="code">Code</Label>
                <Input id="code" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" required autoFocus
                  value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} />
              </div>
            </>
          )}

          {typeof error === 'string' ? (
            <p role="alert" className="rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">{error}</p>
          ) : error ? <ErrorNote error={error} /> : null}
          {notice ? <p className="text-sm text-muted-foreground">{notice}</p> : null}

          <Button type="submit" className="w-full" disabled={busy}>
            {step === 'email' ? 'Send code' : 'Sign in'}
          </Button>

          {step === 'code' ? (
            <div className="flex justify-between text-sm">
              <button type="button" className="underline" onClick={() => { setStep('email'); setCode(''); setError(null); setNotice(null) }}>
                Use another email
              </button>
              <button type="button" className="underline disabled:no-underline disabled:opacity-60" disabled={busy || resendIn > 0} onClick={() => void sendCode()}>
                {resendIn > 0 ? `Send a new code in ${resendIn} s` : 'Send a new code'}
              </button>
            </div>
          ) : null}
        </form>
      </div>
    </div>
  )
}
