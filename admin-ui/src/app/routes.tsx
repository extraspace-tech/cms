import type { ComponentType } from 'react'
import type { RouteObject } from 'react-router'
import { Button } from '@/components/ui/button'
import { useSession } from '@/features/auth/session'
import { AuditPage } from '@/features/audit/AuditPage'
import { DashboardPage } from '@/features/dashboard/DashboardPage'
import { RolesPage } from '@/features/roles/RolesPage'
import { SitePage } from '@/features/sites/SitePage'
import { SitesPage } from '@/features/sites/SitesPage'
import { StaffPage } from '@/features/staff/StaffPage'
import { StorageTypePage } from '@/features/storage-types/StorageTypePage'
import { StorageTypesPage } from '@/features/storage-types/StorageTypesPage'
import { UnitSizePage } from '@/features/unit-sizes/UnitSizePage'
import { UnitSizesPage } from '@/features/unit-sizes/UnitSizesPage'
import { UnitPage } from '@/features/units/UnitPage'
import { UnitsPage } from '@/features/units/UnitsPage'
import { Shell } from './Shell'

function Message({ text }: { text: string }) {
  return <p className="py-16 text-center text-sm text-muted-foreground">{text}</p>
}

/** Hiding only: a page the staff member may not use shows a message; its API calls would get 403 anyway. */
function guarded(permission: string, Page: ComponentType): ComponentType {
  return function Guarded() {
    return useSession().can(permission) ? <Page /> : <Message text="You do not have access to this page." />
  }
}

/** A page that threw while rendering: our own message instead of the router's default error screen. */
function RouteError() {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-3 p-6 text-center">
      <p className="text-sm text-muted-foreground">Something went wrong. Reload the page.</p>
      <Button onClick={() => window.location.reload()}>Reload</Button>
    </div>
  )
}

/** The shell around the given pages. Tests pass their own pages. */
export function shellRoutes(children: RouteObject[]): RouteObject[] {
  return [{ path: '/', Component: Shell, ErrorBoundary: RouteError, children }]
}

export const routes = shellRoutes([
  { index: true, Component: DashboardPage },
  { path: 'staff', Component: guarded('staff.manage', StaffPage) },
  { path: 'roles', Component: guarded('roles.manage', RolesPage) },
  { path: 'audit', Component: guarded('audit.read', AuditPage) },
  { path: 'sites', Component: SitesPage },
  { path: 'sites/:id', Component: SitePage },
  { path: 'storage-types', Component: StorageTypesPage },
  { path: 'storage-types/:id', Component: StorageTypePage },
  { path: 'unit-sizes', Component: UnitSizesPage },
  { path: 'unit-sizes/:id', Component: UnitSizePage },
  { path: 'units', Component: UnitsPage },
  { path: 'units/:id', Component: UnitPage },
  { path: '*', Component: () => <Message text="This page does not exist." /> },
])
