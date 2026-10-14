import { LogOut } from 'lucide-react'
import { NavLink, Outlet } from 'react-router'
import { toastError } from '@/components/ErrorNote'
import { Button } from '@/components/ui/button'
import { useSession, useSignOut } from '@/features/auth/session'
import { cn } from '@/lib/utils'
import { navGroups } from './nav'
import { ThemeSwitch } from './ThemeSwitch'

const itemClass = 'flex items-center gap-2.5 rounded-lg px-3 py-2 text-sm font-medium text-muted-foreground hover:bg-muted hover:text-foreground'

export function Shell() {
  const { me, can } = useSession()
  const signOut = useSignOut()
  const groups = navGroups
    .map((group) => ({ ...group, items: group.items.filter((item) => !item.permission || can(item.permission)) }))
    .filter((group) => group.items.length > 0)

  return (
    <div className="flex h-screen bg-muted/40">
      <aside className="hidden w-64 shrink-0 flex-col border-r bg-background md:flex">
        <div className="flex h-16 items-center gap-2 border-b px-4">
          <div className="flex size-8 items-center justify-center rounded-lg bg-primary text-sm font-bold text-primary-foreground">e</div>
          <div className="leading-tight">
            <p className="text-sm font-bold">ExtraSpace</p>
            <p className="text-[11px] text-muted-foreground">Self Storage</p>
          </div>
        </div>
        <nav className="flex-1 space-y-5 overflow-y-auto px-3 py-4">
          {groups.map((group, index) => (
            <div key={group.label ?? index} className="space-y-1">
              {group.label ? <p className="px-3 pb-1 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">{group.label}</p> : null}
              {group.items.map(({ label, to, icon: Icon, external }) =>
                external ? (
                  <a key={to} href={to} className={itemClass}><Icon className="size-4" />{label}</a>
                ) : (
                  <NavLink key={to} to={to} end={to === '/'} className={({ isActive }) => cn(itemClass, isActive && 'bg-primary/10 text-primary')}>
                    <Icon className="size-4" />{label}
                  </NavLink>
                ))}
            </div>
          ))}
        </nav>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 shrink-0 items-center justify-end gap-3 border-b bg-background px-6">
          <ThemeSwitch />
          <div className="text-right leading-tight">
            <p className="text-sm font-medium">{me.displayName ?? me.email}</p>
            <p className="text-xs text-muted-foreground">{me.roleName}</p>
          </div>
          <Button variant="outline" size="sm" onClick={() => void signOut().catch(toastError)}>
            <LogOut className="size-4" />Sign out
          </Button>
        </header>
        <main className="flex-1 overflow-y-auto p-6"><Outlet /></main>
      </div>
    </div>
  )
}
