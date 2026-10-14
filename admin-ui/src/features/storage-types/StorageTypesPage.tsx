import { YesNo } from '@/components/cells'
import { columnsFor } from '@/components/DataTable'
import { ListPage } from '@/components/ListPage'
import { useSession } from '@/features/auth/session'
import { useListParams } from '@/lib/list'
import { useStorageTypes, useStorageTypeWrites, type StorageType } from './api'

const helper = columnsFor<StorageType>()
const columns = helper.columns([
  helper.accessor('key', { header: 'Key' }),
  helper.accessor('name', { header: 'Name' }),
  helper.accessor('active', { header: 'Active', cell: (c) => <YesNo value={c.getValue()} /> }),
  helper.accessor('sortOrder', { header: 'Sort Order' }),
])

export function StorageTypesPage() {
  const list = useListParams()
  const types = useStorageTypes(list.page, list.search)
  const { remove } = useStorageTypeWrites()
  const write = useSession().can('sites.manage')
  return (
    <ListPage title="Storage Types" noun="storage types" singular="storage type" list={list} query={types} columns={columns}
      rowId={(t) => t.id} rowLabel={(t) => t.key} rowTo={(t) => `/storage-types/${t.id}`}
      newTo={write ? '/storage-types/new' : undefined} onDelete={write ? (t) => remove.mutateAsync(t) : undefined} />
  )
}
