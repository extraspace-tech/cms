import { Box, Grid3x3, LayoutDashboard, MapPin, Maximize, ScrollText, ShieldCheck, Timer, Users, type LucideIcon } from 'lucide-react'

export type NavItem = {
  label: string
  to: string
  icon: LucideIcon
  /** Hidden without it. */
  permission?: string
  /** A page outside this app (a full page load, not a route). */
  external?: boolean
}

export type NavGroup = { label?: string; items: NavItem[] }

const byLabel = (a: NavItem, b: NavItem) => a.label.localeCompare(b.label)

/** The demo's sidebar (S1 spec §11.1): Dashboard first, then one section per group, items alphabetical inside a group. */
export const navGroups: NavGroup[] = [
  { items: [{ label: 'Dashboard', to: '/', icon: LayoutDashboard }] },
  {
    label: 'Platform',
    items: [
      { label: 'Sites', to: '/sites', icon: MapPin },
      { label: 'Storage Types', to: '/storage-types', icon: Grid3x3 },
      { label: 'Unit Sizes', to: '/unit-sizes', icon: Maximize },
      { label: 'Units', to: '/units', icon: Box },
    ].sort(byLabel),
  },
  {
    label: 'Access Control',
    items: [
      { label: 'Admin Users', to: '/staff', icon: Users, permission: 'staff.manage' },
      { label: 'Audit Log', to: '/audit', icon: ScrollText, permission: 'audit.read' },
      { label: 'Jobs', to: '/admin/jobs', icon: Timer, permission: 'jobs.manage', external: true },
      { label: 'Roles', to: '/roles', icon: ShieldCheck, permission: 'roles.manage' },
    ].sort(byLabel),
  },
]
