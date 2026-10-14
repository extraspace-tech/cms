import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { recordReloadedNote } from '@/lib/forms'
import { staffMe } from '@/test/data'
import { problem } from '@/test/problems'
import { renderApp } from '@/test/render'
import { server } from '@/test/server'

const row = { id: '0199e000-0000-7000-8000-0000000000a1', locationCode: 'LSETUP', name: 'Setup site', facilityType: 'SelfStorage', city: null, country: 'SG', active: true, version: 7 }
const site = {
  ...row, siteLinkSiteId: null, displayName: null, description: null, heroImageUrl: null, address1: null, region: null, postalCode: null,
  phone: null, latitude: 1.3, longitude: 103.8, timeZone: 'Asia/Singapore', currency: 'SGD', taxPercentage: 9, taxLabel: 'GST', stripeAccountId: null,
}

function serveList(items = [row]) {
  const queries: URLSearchParams[] = []
  server.use(http.get('*/admin/api/sites', ({ request }) => {
    const query = new URL(request.url).searchParams
    queries.push(query)
    return HttpResponse.json({ items, total: items.length, page: Number(query.get('page') ?? 1), pageSize: 20 })
  }))
  return queries
}

describe('Sites', () => {
  it('lists sites with the demo cells and the row count', async () => {
    serveList()
    renderApp('/admin/sites')
    expect(await screen.findByText('LSETUP')).toBeInTheDocument()
    expect(screen.getByText('1–1 of 1')).toBeInTheDocument()
    expect(screen.getByText('—')).toBeInTheDocument()
    expect(screen.getByText('Yes')).toBeInTheDocument()
  })

  it('searches as you type', async () => {
    const queries = serveList()
    const { user } = renderApp('/admin/sites')
    await user.type(await screen.findByLabelText('Search sites'), 'setup')
    await waitFor(() => expect(queries.at(-1)?.get('search')).toBe('setup'))
  })

  it('saves only the changed fields, clears an emptied number and sends the version', async () => {
    serveList()
    let body: unknown
    server.use(
      http.get('*/admin/api/sites/:id', () => HttpResponse.json(site)),
      http.patch('*/admin/api/sites/:id', async ({ request }) => { body = await request.json(); return HttpResponse.json(site) }),
    )
    const { user } = renderApp(`/admin/sites/${site.id}`)
    const name = await screen.findByLabelText('Name *')
    await user.clear(name)
    await user.type(name, 'New name')
    await user.clear(screen.getByLabelText('Latitude'))
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(body).toEqual({ name: 'New name', clear: ['latitude'], version: 7 }))
    expect(await screen.findByText('LSETUP')).toBeInTheDocument()   // back on the list
  })

  it('shows the server title after a 409, reloads the record and keeps the typed value', async () => {
    serveList()
    let reads = 0
    server.use(
      http.get('*/admin/api/sites/:id', () => { reads++; return HttpResponse.json(site) }),
      http.patch('*/admin/api/sites/:id', () => problem(409, 'CONFLICT', 'The record was changed by someone else.')),
    )
    const { user } = renderApp(`/admin/sites/${site.id}`)
    await user.type(await screen.findByLabelText('City'), 'Singapore')
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    expect(await screen.findByText('The record was changed by someone else.')).toBeInTheDocument()
    expect(screen.getByText(recordReloadedNote)).toBeInTheDocument()
    await waitFor(() => expect(reads).toBe(2))
    expect(screen.getByLabelText('City')).toHaveValue('Singapore')
  })

  it('requires the time zone and the tax label on an edit', async () => {
    serveList()
    let patched = false
    server.use(
      http.get('*/admin/api/sites/:id', () => HttpResponse.json(site)),
      http.patch('*/admin/api/sites/:id', () => { patched = true; return HttpResponse.json(site) }),
    )
    const { user } = renderApp(`/admin/sites/${site.id}`)
    await user.clear(await screen.findByLabelText('Time Zone *'))
    await user.clear(screen.getByLabelText('Tax Label *'))
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    expect(await screen.findAllByText('Required.')).toHaveLength(2)
    expect(patched).toBe(false)
  })

  it('creates a site with only the filled fields', async () => {
    serveList()
    let body: unknown
    server.use(http.post('*/admin/api/sites', async ({ request }) => { body = await request.json(); return HttpResponse.json(site, { status: 201 }) }))
    const { user } = renderApp('/admin/sites/new')
    await user.type(await screen.findByLabelText('Location Code *'), 'L099')
    await user.type(screen.getByLabelText('Name *'), 'Tampines')
    await user.selectOptions(screen.getByLabelText('Country *'), 'SG')
    await user.type(screen.getByLabelText('Tax % *'), '9')
    await user.click(screen.getByRole('button', { name: 'Create' }))
    await waitFor(() => expect(body).toEqual({ locationCode: 'L099', facilityType: 'SelfStorage', name: 'Tampines', country: 'SG', taxPercentage: 9, active: true }))
  })

  it('deletes after a confirm, sending the row version', async () => {
    serveList()
    let url = ''
    server.use(http.delete('*/admin/api/sites/:id', ({ request }) => { url = request.url; return new HttpResponse(null, { status: 204 }) }))
    const { user } = renderApp('/admin/sites')
    await user.click(await screen.findByRole('button', { name: 'Delete site LSETUP' }))
    await user.click(screen.getByRole('button', { name: 'Delete' }))
    await waitFor(() => expect(url).toContain(`/admin/api/sites/${row.id}?version=7`))
  })

  it('closes the delete dialog after a 409, shows the server title and reloads the list', async () => {
    const queries = serveList()
    let deletes = 0
    server.use(http.delete('*/admin/api/sites/:id', () => { deletes++; return problem(409, 'CONFLICT', 'The record was changed by someone else.') }))
    const { user } = renderApp('/admin/sites')
    await user.click(await screen.findByRole('button', { name: 'Delete site LSETUP' }))
    const reads = queries.length
    await user.click(screen.getByRole('button', { name: 'Delete' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByText('The record was changed by someone else.')).toBeInTheDocument()   // a toast: the dialog is gone
    await waitFor(() => expect(queries.length).toBeGreaterThan(reads))
    expect(deletes).toBe(1)
  })

  // Display only: the create test's body has no currency.
  it('shows the currency of the chosen country', async () => {
    serveList()
    const { user } = renderApp('/admin/sites/new')
    const currency = await screen.findByLabelText('Currency')
    expect(currency).toHaveValue('—')
    await user.selectOptions(screen.getByLabelText('Country *'), 'MY')
    expect(currency).toHaveValue('MYR')
    await user.selectOptions(screen.getByLabelText('Country *'), 'KR')
    expect(currency).toHaveValue('KRW')
  })

  it('shows a site read-only to staff without sites.manage', async () => {
    serveList()
    server.use(http.get('*/admin/api/sites/:id', () => HttpResponse.json(site)))
    renderApp(`/admin/sites/${site.id}`, { me: staffMe })
    expect(await screen.findByText('This record is read-only.')).toBeInTheDocument()
    expect(screen.getByLabelText('Name *')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument()
  })

  it('is read-only for staff without sites.manage', async () => {
    serveList()
    server.use(http.get('*/admin/api/sites/:id', () => HttpResponse.json(site)))
    renderApp('/admin/sites', { me: staffMe })
    expect(await screen.findByText('LSETUP')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'New' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Delete site/ })).not.toBeInTheDocument()
  })
})
