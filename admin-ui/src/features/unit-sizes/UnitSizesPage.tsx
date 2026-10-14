import { Dash } from '@/components/cells'
import { columnsFor } from '@/components/DataTable'
import { ListPage } from '@/components/ListPage'
import { useSession } from '@/features/auth/session'
import { useListParams } from '@/lib/list'
import { useUnitSizes, useUnitSizeWrites, type UnitSize } from './api'

const helper = columnsFor<UnitSize>()
const columns = helper.columns([
  helper.accessor('key', { header: 'Key' }),
  helper.accessor('label', { header: 'Label' }),
  helper.accessor('unitGroup', { header: 'Unit Group', cell: (c) => <Dash value={c.getValue()} /> }),
  helper.accessor('sortOrder', { header: 'Sort Order' }),
])

export function UnitSizesPage() {
  const list = useListParams()
  const sizes = useUnitSizes(list.page, list.search)
  const { remove } = useUnitSizeWrites()
  const write = useSession().can('sites.manage')
  return (
    <ListPage title="Unit Sizes" noun="unit sizes" singular="unit size" list={list} query={sizes} columns={columns}
      rowId={(s) => s.id} rowLabel={(s) => s.key} rowTo={(s) => `/unit-sizes/${s.id}`}
      newTo={write ? '/unit-sizes/new' : undefined} onDelete={write ? (s) => remove.mutateAsync(s) : undefined} />
  )
}
