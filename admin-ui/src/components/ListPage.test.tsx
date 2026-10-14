import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import { createMemoryRouter, Link, RouterProvider, useSearchParams } from 'react-router'
import { useListParams } from '@/lib/list'
import { columnsFor } from './DataTable'
import { ListPage, type Page } from './ListPage'

type Row = { id: string; name: string }
const helper = columnsFor<Row>()
const columns = helper.columns([helper.accessor('name', { header: 'Name' })])

/** A list of `total` rows served 20 to a page, like the API. */
function Rows({ total }: { total: number }) {
  const list = useListParams()
  const [params] = useSearchParams()
  const query = useQuery<Page<Row>>({
    queryKey: ['rows', list.page, list.search],
    queryFn: async () => {
      const first = (list.page - 1) * 20
      const items = Array.from({ length: Math.max(0, Math.min(20, total - first)) }, (_, i) => ({ id: String(first + i), name: `Row ${first + i + 1}` }))
      return { items, total, page: list.page, pageSize: 20 }
    },
  })
  return (
    <>
      <output aria-label="address">{params.toString()}</output>
      <Link to="/rows">All rows</Link>
      <ListPage title="Rows" noun="rows" singular="row" list={list} query={query} columns={columns}
        rowId={(r) => r.id} rowLabel={(r) => r.name} rowTo={(r) => `/rows/${r.id}`} />
    </>
  )
}

function mount(path: string, total = 45) {
  const router = createMemoryRouter([{ path: '/rows', element: <Rows total={total} /> }], { initialEntries: [path] })
  render(<QueryClientProvider client={new QueryClient()}><RouterProvider router={router} /></QueryClientProvider>)
  return userEvent.setup()
}

it('puts the search in the address and starts again at page 1', async () => {
  const user = mount('/rows?page=2')
  expect(await screen.findByText('21–40 of 45')).toBeInTheDocument()
  await user.type(screen.getByLabelText('Search rows'), 'ab')
  await waitFor(() => expect(screen.getByLabelText('address')).toHaveTextContent('search=ab'))
  expect(screen.getByLabelText('address')).not.toHaveTextContent('page=')
})

it('follows an address change from outside the box, and the old text does not come back', async () => {
  const user = mount('/rows')
  await user.type(await screen.findByLabelText('Search rows'), 'ab')
  await waitFor(() => expect(screen.getByLabelText('address')).toHaveTextContent('search=ab'))
  await user.click(screen.getByRole('link', { name: 'All rows' }))
  expect(screen.getByLabelText('Search rows')).toHaveValue('')
  await new Promise((resolve) => setTimeout(resolve, 450))   // longer than the 300 ms search delay
  expect(screen.getByLabelText('Search rows')).toHaveValue('')
  expect(screen.getByLabelText('address')).toBeEmptyDOMElement()
})

it('steps back to the last page when the address is past it', async () => {
  mount('/rows?page=9', 40)
  expect(await screen.findByText('21–40 of 40')).toBeInTheDocument()
  expect(screen.getByLabelText('address')).toHaveTextContent('page=2')
  expect(screen.queryByText('No rows yet')).not.toBeInTheDocument()
})

it('reads a fractional page as a whole one', async () => {
  mount('/rows?page=2.5')
  expect(await screen.findByText('21–40 of 45')).toBeInTheDocument()
})
