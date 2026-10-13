import type { ReactNode } from 'react'
import { createColumnHelper, tableFeatures, useTable, type RowData, type TableOptions } from '@tanstack/react-table'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

/** One feature set for every list. Sorting, filtering and paging are done by the server (or not at all) in S0. */
export const features = tableFeatures({})

export function columnsFor<T extends RowData>() {
  return createColumnHelper<typeof features, T>()
}

// What `useTable` itself takes; `columnsFor<T>().columns([...])` produces it.
type Columns<T extends RowData> = TableOptions<typeof features, T>['columns']

/** `rowId` keys the rows by record, not by position. A falsy `empty` shows no placeholder row (the page shows its own error). */
export function DataTable<T extends RowData>({ columns, data, rowId, empty }: { columns: Columns<T>; data: T[]; rowId: (row: T) => string; empty?: ReactNode }) {
  const table = useTable({ features, columns, data, getRowId: rowId })
  const rows = table.getRowModel().rows
  return (
    <div className="rounded-xl border bg-card">
      <Table>
        <TableHeader>
          {table.getHeaderGroups().map((group) => (
            <TableRow key={group.id}>
              {group.headers.map((header) => (
                <TableHead key={header.id}>{header.isPlaceholder ? null : <table.FlexRender header={header} />}</TableHead>
              ))}
            </TableRow>
          ))}
        </TableHeader>
        <TableBody>
          {rows.length === 0 ? (
            empty ? (
              <TableRow>
                <TableCell colSpan={table.getAllColumns().length} className="py-10 text-center text-sm text-muted-foreground">{empty}</TableCell>
              </TableRow>
            ) : null
          ) : (
            rows.map((row) => (
              <TableRow key={row.id}>
                {row.getAllCells().map((cell) => (
                  <TableCell key={cell.id}><table.FlexRender cell={cell} /></TableCell>
                ))}
              </TableRow>
            ))
          )}
        </TableBody>
      </Table>
    </div>
  )
}
