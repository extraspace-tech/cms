import type { components } from '@/api/schema'

type Me = components['schemas']['AdminMeResponse']

export const OWNER_ROLE_ID = '0199dd00-0000-7000-8000-000000000001'
export const DESK_ROLE_ID = '0199dd00-0000-7000-8000-0000000000aa'

export const ownerMe: Me = {
  id: '0199dd00-0000-7000-8000-000000000101', email: 'owner@example.test', displayName: 'Olive Owner',
  roleId: OWNER_ROLE_ID, roleName: 'Owner', isOwner: true,
  permissions: ['staff.manage', 'roles.manage', 'audit.read', 'jobs.manage', 'sites.manage'],
}

export const staffMe: Me = {
  id: '0199dd00-0000-7000-8000-000000000102', email: 'desk@example.test', displayName: null,
  roleId: DESK_ROLE_ID, roleName: 'Front desk', isOwner: false, permissions: ['staff.manage'],
}
