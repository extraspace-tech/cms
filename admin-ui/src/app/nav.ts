import { LayoutDashboard, ScrollText, ShieldCheck, Timer, Users, type LucideIcon } from 'lucide-react'

export type NavItem = {
  label: string
  to: string
  icon: LucideIcon
  /** Hidden without it. */
  permission?: string
  /** A page outside this app (a full page load, not a route). */
  external?: boolean
}

export const navItems: NavItem[] = [
  { label: 'Dashboard', to: '/', icon: LayoutDashboard },
  { label: 'Staff', to: '/staff', icon: Users, permission: 'staff.manage' },
  { label: 'Roles', to: '/roles', icon: ShieldCheck, permission: 'roles.manage' },
  { label: 'Audit log', to: '/audit', icon: ScrollText, permission: 'audit.read' },
  { label: 'Jobs', to: '/admin/jobs', icon: Timer, permission: 'jobs.manage', external: true },
]
