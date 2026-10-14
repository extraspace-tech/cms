import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { z } from 'zod'
import { ApiError } from '@/api/errors'
import { TRACE } from '@/test/problems'
import { RecordEditor } from './RecordEditor'
import type { FieldSpec } from './RecordFields'

const schema = z.object({ name: z.string().trim().min(1, 'Required.'), city: z.string() })
type Values = z.infer<typeof schema>
type Row = { id: string; version: number; name: string; city: string }
const fields: FieldSpec<Values>[] = [{ name: 'name', label: 'Name', required: true }, { name: 'city', label: 'City' }]

function conflict(title: string) {
  return ApiError.from(new Response(null, { status: 409 }), { title, status: 409, code: 'CONFLICT', traceId: TRACE })
}

/** Holds the record like a page does: the first save fails with `failure`, and the page reloads the record as `reloaded`. */
function Host({ failure, reloaded, saved }: { failure: ApiError; reloaded: Row; saved: (body: Record<string, unknown>) => void }) {
  const [row, setRow] = useState<Row>({ id: 'r1', version: 7, name: 'Old name', city: 'Old city' })
  const [failed, setFailed] = useState(false)
  return (
    <RecordEditor backTo="/list" backLabel="List" title="Record" record={row} schema={schema} defaults={{ name: row.name, city: row.city }}
      fields={fields} canWrite create={async () => null}
      update={async (_id, body) => {
        if (!failed) {
          setFailed(true)
          setRow(reloaded)
          throw failure
        }
        saved(body)
      }} />
  )
}

function mount(element: React.ReactNode) {
  const router = createMemoryRouter([{ path: '/record', element }, { path: '/list', element: <p>The list</p> }], { initialEntries: ['/record'] })
  render(<RouterProvider router={router} />)
  return userEvent.setup()
}

it('after a 409 keeps what the user typed, shows the reloaded values elsewhere, and saves against the new version', async () => {
  let body: unknown
  const user = mount(<Host failure={conflict('The record was changed by someone else. Reload and try again.')}
    reloaded={{ id: 'r1', version: 8, name: 'Old name', city: 'Their city' }} saved={(b) => { body = b }} />)
  const name = await screen.findByLabelText('Name *')
  await user.clear(name)
  await user.type(name, 'My name')
  await user.click(screen.getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('The record was changed by someone else. Reload and try again.')).toBeInTheDocument()
  expect(screen.getByText('The record was reloaded; your changes are kept.')).toBeInTheDocument()
  expect(screen.getByLabelText('Name *')).toHaveValue('My name')
  expect(screen.getByLabelText('City')).toHaveValue('Their city')

  await user.click(screen.getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByText('The list')).toBeInTheDocument()
  expect(body).toEqual({ name: 'My name', version: 8 })
})

it('shows the server title and trace id for a 409 that is not a stale version', async () => {
  const user = mount(<Host failure={conflict('A record with the same value already exists.')}
    reloaded={{ id: 'r1', version: 7, name: 'Old name', city: 'Old city' }} saved={() => {}} />)
  await user.type(await screen.findByLabelText('City'), ' 2')
  await user.click(screen.getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByText('A record with the same value already exists.')).toBeInTheDocument()
  expect(screen.getByText(new RegExp(TRACE))).toBeInTheDocument()
  expect(screen.queryByText(/Someone else changed/)).not.toBeInTheDocument()
})

it('never sends a derived field, even when it is not marked read-only', async () => {
  const withCode = z.object({ name: z.string().trim().min(1, 'Required.'), code: z.string() })
  let body: unknown
  const user = mount(
    <RecordEditor backTo="/list" backLabel="List" title="Record" schema={withCode} defaults={{ name: '', code: 'N-' }}
      fields={[{ name: 'name', label: 'Name', required: true }, { name: 'code', label: 'Code', derive: (v) => `N-${v.name}` }]}
      canWrite create={async (b) => { body = b }} update={async () => null} />,
  )
  await user.type(await screen.findByLabelText('Name *'), 'Bukit')
  expect(screen.getByLabelText('Code')).toHaveValue('N-Bukit')
  await user.click(screen.getByRole('button', { name: 'Create' }))
  expect(await screen.findByText('The list')).toBeInTheDocument()
  expect(body).toEqual({ name: 'Bukit' })
})
