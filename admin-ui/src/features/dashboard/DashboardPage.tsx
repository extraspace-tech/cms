import { PageShell } from '@/components/PageShell'
import { useSession } from '@/features/auth/session'

export function DashboardPage() {
  const { me } = useSession()
  return (
    <PageShell title="Dashboard">
      <p className="text-sm text-muted-foreground">Welcome, {me.displayName ?? me.email}. Reports arrive with the self-storage release.</p>
    </PageShell>
  )
}
