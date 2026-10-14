import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { renderApp } from '@/test/render'
import { server } from '@/test/server'

const types = [{ id: '0199e000-0000-7000-8000-000000000001', key: 'self-storage', name: 'Self Storage', displayName: null, iconPath: null, imagePath: null, sortOrder: 1, active: true, version: 1 }]
const size = {
  id: '0199e000-0000-7000-8000-0000000000b1', key: 'small', label: 'Small', displayName: null, description: null, unitGroup: 'S', imagePath: null,
  minAreaSqft: 0, maxAreaSqft: 50, sortOrder: 1, storageTypeId: types[0].id, storageTypeName: 'Self Storage', version: 5,
}

function serve() {
  const state: { body?: unknown } = {}
  server.use(
    http.get('*/admin/api/storage-types', () => HttpResponse.json({ items: types, total: 1, page: 1, pageSize: 100 })),
    http.get('*/admin/api/unit-sizes', () => HttpResponse.json({ items: [size], total: 1, page: 1, pageSize: 20 })),
    http.get('*/admin/api/unit-sizes/:id', () => HttpResponse.json(size)),
    http.patch('*/admin/api/unit-sizes/:id', async ({ request }) => { state.body = await request.json(); return HttpResponse.json(size) }),
  )
  return state
}

it('clears the storage type and the max area when they are emptied', async () => {
  const state = serve()
  const { user } = renderApp(`/admin/unit-sizes/${size.id}`)
  await screen.findByDisplayValue('Small')
  expect(screen.getByLabelText('Storage Type')).toHaveValue(types[0].id)
  await user.selectOptions(screen.getByLabelText('Storage Type'), '')
  await user.clear(screen.getByLabelText('Max Area (sq ft)'))
  await user.click(screen.getByRole('button', { name: 'Save changes' }))
  await waitFor(() => expect(state.body).toEqual({ clear: ['maxAreaSqft', 'storageTypeId'], version: 5 }))   // form field order
})

it('refuses a min area above the max area', async () => {
  const state = serve()
  const { user } = renderApp(`/admin/unit-sizes/${size.id}`)
  const min = await screen.findByLabelText('Min Area (sq ft)')
  await user.clear(min)
  await user.type(min, '60')
  await user.click(screen.getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByText('Must not be above the max area.')).toBeInTheDocument()
  expect(state.body).toBeUndefined()
})

it('lists unit sizes with a dash for an empty unit group', async () => {
  serve()
  server.use(http.get('*/admin/api/unit-sizes', () => HttpResponse.json({ items: [{ ...size, unitGroup: null }], total: 1, page: 1, pageSize: 20 })))
  renderApp('/admin/unit-sizes')
  expect(await screen.findByText('small')).toBeInTheDocument()
  expect(screen.getByText('—')).toBeInTheDocument()
})

it('refuses a sort order outside int32 and an area with 3 decimals', async () => {
  const state = serve()
  const { user } = renderApp(`/admin/unit-sizes/${size.id}`)
  const order = await screen.findByLabelText('Sort Order *')
  await user.clear(order)
  await user.type(order, '2147483648')
  const min = screen.getByLabelText('Min Area (sq ft)')
  await user.clear(min)
  await user.type(min, '1.005')
  await user.click(screen.getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByText('Enter a number from -2147483647 to 2147483647.')).toBeInTheDocument()
  expect(screen.getByText('At most 2 decimals.')).toBeInTheDocument()
  expect(state.body).toBeUndefined()
})

it('accepts an area with 2 decimals', async () => {
  const state = serve()
  const { user } = renderApp(`/admin/unit-sizes/${size.id}`)
  const max = await screen.findByLabelText('Max Area (sq ft)')
  await user.clear(max)
  await user.type(max, '50.25')
  await user.click(screen.getByRole('button', { name: 'Save changes' }))
  await waitFor(() => expect(state.body).toEqual({ maxAreaSqft: 50.25, version: 5 }))
})
