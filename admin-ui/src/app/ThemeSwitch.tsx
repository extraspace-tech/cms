import { useSyncExternalStore } from 'react'
import { Monitor, Moon, Sun } from 'lucide-react'
import { getTheme, setTheme, subscribeTheme, type ThemePref } from '@/lib/theme'
import { cn } from '@/lib/utils'

const options: [ThemePref, string, typeof Sun][] = [
  ['light', 'Light', Sun],
  ['dark', 'Dark', Moon],
  ['system', 'Match system', Monitor],
]

export function ThemeSwitch() {
  const pref = useSyncExternalStore(subscribeTheme, getTheme)
  return (
    <div role="group" aria-label="Theme" className="flex items-center rounded-lg border bg-muted p-0.5">
      {options.map(([value, label, Icon]) => (
        <button key={value} type="button" title={label} aria-label={label} aria-pressed={pref === value} onClick={() => setTheme(value)}
          className={cn('flex size-7 items-center justify-center rounded-md text-muted-foreground', pref === value && 'bg-background text-foreground shadow-sm')}>
          <Icon className="size-3.5" />
        </button>
      ))}
    </div>
  )
}
