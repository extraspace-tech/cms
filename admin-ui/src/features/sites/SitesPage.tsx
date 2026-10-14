import { Dash, StatusBadge, YesNo } from '@/components/cells'
import { columnsFor } from '@/components/DataTable'
import { ListPage } from '@/components/ListPage'
import { useSession } from '@/features/auth/session'
import { useListParams } from '@/lib/list'
import { useSites, useSiteWrites, type SiteListItem } from './api'

export const facilityLabels: Record<string, string> = { SelfStorage: 'Self Storage', Workspace: 'Workspace' }

const helper = columnsFor<SiteListItem>()
const columns = helper.columns([
  helper.accessor('locationCode', { header: 'Location Code' }),
  helper.accessor('name', { header: 'Name' }),
  helper.accessor('facilityType', { header: 'Facility Type', cell: (c) => <StatusBadge value={facilityLabels[c.getValue()] ?? c.getValue()} /> }),
  helper.accessor('city', { header: 'City', cell: (c) => <Dash value={c.getValue()} /> }),
  helper.accessor('country', { header: 'Country' }),
  helper.accessor('active', { header: 'Active', cell: (c) => <YesNo value={c.getValue()} /> }),
])

export function SitesPage() {
  const list = useListParams()
  const sites = useSites(list.page, list.search)
  const { remove } = useSiteWrites()
  const write = useSession().can('sites.manage')
  return (
    <ListPage title="Sites" noun="sites" singular="site" list={list} query={sites} columns={columns}
      rowId={(s) => s.id} rowLabel={(s) => s.locationCode} rowTo={(s) => `/sites/${s.id}`}
      newTo={write ? '/sites/new' : undefined} onDelete={write ? (s) => remove.mutateAsync(s) : undefined} />
  )
}
