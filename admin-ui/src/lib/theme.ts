export type ThemePref = 'light' | 'dark' | 'system'

const KEY = 'es-admin-theme'
const SYSTEM_DARK = '(prefers-color-scheme: dark)'
const listeners = new Set<() => void>()
let watching = false

export function getTheme(): ThemePref {
  const stored = localStorage.getItem(KEY)
  return stored === 'light' || stored === 'dark' ? stored : 'system'
}

export function applyTheme(): void {
  const pref = getTheme()
  const dark = pref === 'dark' || (pref === 'system' && window.matchMedia(SYSTEM_DARK).matches)
  document.documentElement.classList.toggle('dark', dark)
}

export function setTheme(pref: ThemePref): void {
  if (pref === 'system') localStorage.removeItem(KEY)
  else localStorage.setItem(KEY, pref)
  refresh()
}

function refresh(): void {
  applyTheme()
  for (const listener of listeners) listener()
}

/** Follows an OS theme switch while the choice is `system`. Registers one listener, however often it is called. */
export function watchSystemTheme(): void {
  if (watching) return
  watching = true
  window.matchMedia(SYSTEM_DARK).addEventListener('change', refresh)
}

export function subscribeTheme(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}
