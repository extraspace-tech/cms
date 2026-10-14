import { Dash, YesNo } from '@/components/cells'
import { columnsFor } from '@/components/DataTable'
import { ListPage } from '@/components/ListPage'
import { NativeSelect } from '@/components/NativeSelect'
import { useSiteOptions } from '@/features/sites/api'
import { useListParams } from '@/lib/list'
import { formatMoney } from '@/lib/money'
import { useUnits, type UnitListItem } from './api'
import { UnitSyncPanel } from './UnitSyncPanel'

const filterNames = ['siteId', 'available'] as const

const helper = columnsFor<UnitListItem>()
const columns = helper.columns([
  helper.accessor('unitName', { header: 'Unit Name' }),
  helper.accessor('siteName', { header: 'Site' }),
  helper.accessor('storageTypeName', { header: 'Storage Type', cell: (c) => <Dash value={c.getValue()} /> }),
  helper.accessor('unitSizeLabel', { header: 'Unit Size', cell: (c) => <Dash value={c.getValue()} /> }),
  helper.accessor('standardRate', { header: 'Standard Rate', cell: (c) => formatMoney(c.getValue(), c.row.original.currency) }),
  helper.accessor('available', { header: 'Available', cell: (c) => <YesNo value={c.getValue()} /> }),
  helper.accessor('rented', { header: 'Rented', cell: (c) => <YesNo value={c.getValue()} /> }),
])

/** Read-only (S1 spec §11.2): units come from SiteLink only. No New, no delete. */
export function UnitsPage() {
  const list = useListParams(filterNames)
  const units = useUnits(list.page, list.search, list.filters.siteId, list.filters.available)
  const sites = useSiteOptions()
  return (
    <ListPage
      title="Units" noun="units" singular="unit" list={list} query={units} columns={columns}
      rowId={(u) => u.id} rowLabel={(u) => u.unitName} rowTo={(u) => `/units/${u.id}`}
      filters={
        <>
          <NativeSelect aria-label="Site" className="w-56" value={list.filters.siteId} onChange={(e) => list.setFilter('siteId', e.target.value)}>
            <option value="">All sites</option>
            {sites.data?.items.map((s) => <option key={s.id} value={s.id}>{s.locationCode} · {s.name}</option>)}
          </NativeSelect>
          <NativeSelect aria-label="Status" className="w-44" value={list.filters.available} onChange={(e) => list.setFilter('available', e.target.value)}>
            <option value="">All statuses</option>
            <option value="true">Available</option>
            <option value="false">Unavailable</option>
          </NativeSelect>
        </>
      }
    >
      <UnitSyncPanel />
    </ListPage>
  )
}
