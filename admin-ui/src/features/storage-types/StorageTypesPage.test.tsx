import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { renderApp } from '@/test/render'
import { server } from '@/test/server'

const type = { id: '0199e000-0000-7000-8000-000000000001', key: 'self-storage', name: 'Self Storage', displayName: null, iconPath: null, imagePath: null, sortOrder: 1, active: true, version: 3 }

it('shows the key read-only on an edit and never sends it', async () => {
  let body: unknown
  server.use(
    http.get('*/admin/api/storage-types', () => HttpResponse.json({ items: [type], total: 1, page: 1, pageSize: 20 })),
    http.get('*/admin/api/storage-types/:id', () => HttpResponse.json(type)),
    http.patch('*/admin/api/storage-types/:id', async ({ request }) => { body = await request.json(); return HttpResponse.json(type) }),
  )
  const { user } = renderApp(`/admin/storage-types/${type.id}`)
  expect(await screen.findByLabelText('Key *')).toHaveAttribute('readonly')
  const sort = screen.getByLabelText('Sort Order *')
  await user.clear(sort)
  await user.type(sort, '4')
  await user.click(screen.getByRole('button', { name: 'Save changes' }))
  await waitFor(() => expect(body).toEqual({ sortOrder: 4, version: 3 }))
})

it('lists storage types and creates one without a sort order', async () => {
  let body: unknown
  server.use(
    http.get('*/admin/api/storage-types', () => HttpResponse.json({ items: [type], total: 1, page: 1, pageSize: 20 })),
    http.post('*/admin/api/storage-types', async ({ request }) => { body = await request.json(); return HttpResponse.json(type, { status: 201 }) }),
  )
  const { user } = renderApp('/admin/storage-types')
  expect(await screen.findByText('self-storage')).toBeInTheDocument()
  await user.click(screen.getByRole('button', { name: 'New' }))
  await user.type(await screen.findByLabelText('Key *'), 'wine')
  await user.type(screen.getByLabelText('Name *'), 'Wine')
  await user.click(screen.getByRole('button', { name: 'Create' }))
  await waitFor(() => expect(body).toEqual({ key: 'wine', name: 'Wine', active: true }))
})
