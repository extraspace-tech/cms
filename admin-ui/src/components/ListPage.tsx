import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useMutation, type UseQueryResult } from '@tanstack/react-query'
import type { RowData } from '@tanstack/react-table'
import { Plus, Trash2 } from 'lucide-react'
import { useNavigate } from 'react-router'
import { ApiError } from '@/api/errors'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { DataTable, type Columns } from '@/components/DataTable'
import { ErrorNote, toastError } from '@/components/ErrorNote'
import { PageShell } from '@/components/PageShell'
import { Pagination } from '@/components/Pagination'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

export type Page<T> = { items: T[]; total: number; page: number; pageSize: number }

type Props<T extends RowData> = {
  title: string
  /** Plural, lower case ("sites"): the search placeholder and the empty state. */
  noun: string
  /** Singular, lower case ("site"): the delete question. */
  singular: string
  list: { search: string; setSearch: (value: string) => void; setPage: (page: number) => void }
  query: UseQueryResult<Page<T>>
  columns: Columns<T>
  rowId: (row: T) => string
  rowLabel: (row: T) => string
  rowTo: (row: T) => string
  searchable?: boolean
  filters?: ReactNode
  /** Set only for staff who may write (hiding only: the server decides). */
  newTo?: string
  onDelete?: (row: T) => Promise<unknown>
  actions?: ReactNode
  children?: ReactNode
}

/** The demo's list page (S1 spec §11.1): search, filters, 20 rows, "start–end of total", row click → record page. */
export function ListPage<T extends RowData>(props: Props<T>) {
  const { title, noun, singular, list, query, columns, rowId, rowLabel, rowTo, searchable = true, filters, newTo, onDelete, actions, children } = props
  const navigate = useNavigate()
  const { search, setSearch, setPage } = list
  const [text, setText] = useState(search)
  const sent = useRef(search)
  const [target, setTarget] = useState<T | null>(null)
  const remove = useMutation({
    mutationFn: (row: T) => onDelete!(row),
    onSuccess: () => setTarget(null),
    // The list reloads after a 409: the dialog closes so that a retry cannot send the old version again.
    onError: (error) => {
      if (!(error instanceof ApiError) || error.status !== 409) return
      setTarget(null)
      toastError(error)
    },
  })

  // The address changed without us (a sidebar click): the box follows it, and the timer below must not put the old text back.
  useEffect(() => {
    if (search === sent.current) return
    sent.current = search
    setText(search)
  }, [search])

  // Search as you type, a moment after the last key.
  useEffect(() => {
    if (text === sent.current) return
    const timer = setTimeout(() => {
      sent.current = text
      setSearch(text)
    }, 300)
    return () => clearTimeout(timer)
  }, [text, setSearch])

  const data = query.data
  const lastPage = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1
  const pastEnd = data !== undefined && data.items.length === 0 && data.total > 0 && data.page > lastPage

  // Past the last page (its last row deleted, or an old address): step back to the last page that has rows.
  useEffect(() => {
    if (pastEnd) setPage(lastPage)
  }, [pastEnd, lastPage, setPage])

  return (
    <PageShell title={title} actions={<>{actions}{newTo ? <Button onClick={() => navigate(newTo)}><Plus className="size-4" />New</Button> : null}</>}>
      {searchable || filters ? (
        <div className="flex flex-wrap items-center gap-2">
          {searchable ? (
            <Input className="max-w-xs" placeholder={`Search ${noun}…`} aria-label={`Search ${noun}`} value={text} onChange={(event) => setText(event.target.value)} />
          ) : null}
          {filters}
        </div>
      ) : null}
      {query.isError ? <ErrorNote error={query.error} /> : null}
      <DataTable
        columns={columns}
        data={data?.items ?? []}
        rowId={rowId}
        onRowClick={(row) => navigate(rowTo(row))}
        rowActions={onDelete ? (row) => (
          <Button size="icon" variant="ghost" aria-label={`Delete ${singular} ${rowLabel(row)}`} onClick={() => { remove.reset(); setTarget(row) }}>
            <Trash2 className="size-4" />
          </Button>
        ) : undefined}
        empty={pastEnd ? null : data ? `No ${noun} yet` : query.isPending ? 'Loading…' : null}
      />
      {data && data.total > 0 && !pastEnd ? <Pagination page={data.page} pageSize={data.pageSize} total={data.total} onPage={setPage} /> : null}
      {children}
      <ConfirmDialog
        open={target !== null}
        title={`Delete ${singular}?`}
        description="This action can't be undone."
        confirmLabel="Delete"
        destructive
        pending={remove.isPending}
        error={remove.error}
        onConfirm={() => { if (target !== null) remove.mutate(target) }}
        onCancel={() => { remove.reset(); setTarget(null) }}
      />
    </PageShell>
  )
}
