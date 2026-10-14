import type { ReactNode } from 'react'
import { createColumnHelper, tableFeatures, useTable, type RowData, type TableOptions } from '@tanstack/react-table'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

/** One feature set for every list. Sorting, filtering and paging are done by the server (or not at all) in S0. */
export const features = tableFeatures({})

export function columnsFor<T extends RowData>() {
  return createColumnHelper<typeof features, T>()
}

// What `useTable` itself takes; `columnsFor<T>().columns([...])` produces it.
export type Columns<T extends RowData> = TableOptions<typeof features, T>['columns']

type Props<T extends RowData> = {
  columns: Columns<T>
  data: T[]
  rowId: (row: T) => string
  empty?: ReactNode
  /** Row click (and Enter on a focused row) opens the record. */
  onRowClick?: (row: T) => void
  /** A trailing cell per row (buttons); its clicks never reach onRowClick. */
  rowActions?: (row: T) => ReactNode
}

/** `rowId` keys the rows by record, not by position. A falsy `empty` shows no placeholder row (the page shows its own error). */
export function DataTable<T extends RowData>({ columns, data, rowId, empty, onRowClick, rowActions }: Props<T>) {
  const table = useTable({ features, columns, data, getRowId: rowId })
  const rows = table.getRowModel().rows
  const width = table.getAllColumns().length + (rowActions ? 1 : 0)
  return (
    <div className="rounded-xl border bg-card">
      <Table>
        <TableHeader>
          {table.getHeaderGroups().map((group) => (
            <TableRow key={group.id}>
              {group.headers.map((header) => (
                <TableHead key={header.id}>{header.isPlaceholder ? null : <table.FlexRender header={header} />}</TableHead>
              ))}
              {rowActions ? <TableHead><span className="sr-only">Actions</span></TableHead> : null}
            </TableRow>
          ))}
        </TableHeader>
        <TableBody>
          {rows.length === 0 ? (
            empty ? (
              <TableRow>
                <TableCell colSpan={width} className="py-10 text-center text-sm text-muted-foreground">{empty}</TableCell>
              </TableRow>
            ) : null
          ) : (
            rows.map((row) => (
              <TableRow
                key={row.id}
                className={onRowClick ? 'cursor-pointer' : undefined}
                tabIndex={onRowClick ? 0 : undefined}
                onClick={onRowClick ? () => onRowClick(row.original) : undefined}
                onKeyDown={onRowClick ? (event) => { if (event.key === 'Enter') onRowClick(row.original) } : undefined}
              >
                {row.getAllCells().map((cell) => (
                  <TableCell key={cell.id}><table.FlexRender cell={cell} /></TableCell>
                ))}
                {rowActions ? (
                  <TableCell className="text-right" onClick={(event) => event.stopPropagation()} onKeyDown={(event) => event.stopPropagation()}>
                    {rowActions(row.original)}
                  </TableCell>
                ) : null}
              </TableRow>
            ))
          )}
        </TableBody>
      </Table>
    </div>
  )
}
