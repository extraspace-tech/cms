import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { formatInstant } from '@/lib/time'
import { staffMe } from '@/test/data'
import { problem } from '@/test/problems'
import { renderApp } from '@/test/render'
import { server } from '@/test/server'

const siteRow = { id: '0199e000-0000-7000-8000-0000000000a1', locationCode: 'L013', name: 'Gasan', facilityType: 'SelfStorage', city: 'Seoul', country: 'KR', active: true, version: 2 }
const unit = { id: '0199e000-0000-7000-8000-0000000000c1', unitName: 'A101', siteId: siteRow.id, siteName: 'Gasan', currency: 'KRW', storageTypeName: 'Self Storage', unitSizeLabel: null, standardRate: 85000.4, available: true, rented: false }
const status = { siteId: siteRow.id, locationCode: 'L013', siteName: 'Gasan', active: true, units: 1, lastSyncedAt: null, lastAttemptAt: '2026-10-14T19:00:05Z', lastError: 'SiteLink could not be reached.' }

function serve() {
  const queries: URLSearchParams[] = []
  const syncs: unknown[] = []
  server.use(
    http.get('*/admin/api/sites', () => HttpResponse.json({ items: [siteRow], total: 1, page: 1, pageSize: 100 })),
    http.get('*/admin/api/units', ({ request }) => {
      queries.push(new URL(request.url).searchParams)
      return HttpResponse.json({ items: [unit], total: 1, page: 1, pageSize: 20 })
    }),
    http.get('*/admin/api/unit-sync', () => HttpResponse.json([status])),
    http.post('*/admin/api/sites/:id/unit-sync', async ({ request }) => { syncs.push(await request.json()); return new HttpResponse(null, { status: 202 }) }),
  )
  return { queries, syncs }
}

describe('Units', () => {
  it('lists units with the site currency and filters by site and status', async () => {
    const { queries } = serve()
    const { user } = renderApp('/admin/units')
    const table = within((await screen.findAllByRole('table'))[0])
    expect(await table.findByText('A101')).toBeInTheDocument()
    expect(table.getByText(/85,000$/)).toBeInTheDocument()   // KRW: no decimals
    await user.selectOptions(screen.getByLabelText('Site'), siteRow.id)
    await user.selectOptions(screen.getByLabelText('Status'), 'false')
    await waitFor(() => {
      expect(queries.at(-1)?.get('siteId')).toBe(siteRow.id)
      expect(queries.at(-1)?.get('available')).toBe('false')
    })
  })

  it('shows the sync state per site and starts a sync or a confirmed full resync', async () => {
    const { syncs } = serve()
    const { user } = renderApp('/admin/units')
    expect(await screen.findByText('SiteLink could not be reached.')).toBeInTheDocument()
    expect(screen.getByText(new RegExp(formatInstant(status.lastAttemptAt)))).toBeInTheDocument()   // when the error happened
    expect(screen.getByText('Never')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Sync now L013' }))
    await user.click(screen.getByRole('button', { name: 'Full resync L013' }))
    await user.click(screen.getByRole('button', { name: 'Start' }))
    await waitFor(() => expect(syncs).toEqual([{ full: false }, { full: true }]))
  })

  it('keeps Full resync disabled while a Sync now is on its way', async () => {
    serve()
    let answer: (response: Response) => void = () => {}
    server.use(http.post('*/admin/api/sites/:id/unit-sync', () => new Promise<Response>((resolve) => { answer = resolve })))
    const { user } = renderApp('/admin/units')
    await user.click(await screen.findByRole('button', { name: 'Sync now L013' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Full resync L013' })).toBeDisabled())
    answer(problem(404, 'NOT_FOUND', 'Site not found.'))
    expect(await screen.findByText('Site not found.')).toBeInTheDocument()   // its error toast still shows
    await waitFor(() => expect(screen.getByRole('button', { name: 'Full resync L013' })).toBeEnabled())
  })

  it('reloads the unit list when a site finishes syncing', async () => {
    const { queries } = serve()
    let synced: string | null = null
    server.use(
      http.get('*/admin/api/unit-sync', () => HttpResponse.json([{ ...status, lastSyncedAt: synced }])),
      http.post('*/admin/api/sites/:id/unit-sync', () => { synced = '2026-10-14T19:10:00Z'; return new HttpResponse(null, { status: 202 }) }),
    )
    const { user } = renderApp('/admin/units')
    expect(await within((await screen.findAllByRole('table'))[0]).findByText('A101')).toBeInTheDocument()
    const before = queries.length
    await user.click(await screen.findByRole('button', { name: 'Sync now L013' }))
    expect(await screen.findByText(formatInstant('2026-10-14T19:10:00Z'))).toBeInTheDocument()
    await waitFor(() => expect(queries.length).toBeGreaterThan(before))
  })

  it('shows why a sync could not start', async () => {
    serve()
    server.use(http.post('*/admin/api/sites/:id/unit-sync', () => problem(404, 'NOT_FOUND', 'Site not found.')))
    const { user } = renderApp('/admin/units')
    await user.click(await screen.findByRole('button', { name: 'Sync now L013' }))
    expect(await screen.findByText('Site not found.')).toBeInTheDocument()
  })

  it('has no sync buttons and no New button without sites.manage', async () => {
    serve()
    renderApp('/admin/units', { me: staffMe })
    expect(await screen.findByText('SiteLink could not be reached.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Sync now/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'New' })).not.toBeInTheDocument()
  })

  it('shows a unit read-only', async () => {
    serve()
    server.use(http.get('*/admin/api/units/:id', () => HttpResponse.json({
      ...unit, width: 5, length: 10, areaSqft: 50, floor: 2, climate: true, webRate: null, deletedAt: null, lastSyncedAt: '2026-10-14T19:00:00Z',
    })))
    renderApp(`/admin/units/${unit.id}`)
    expect(await screen.findByText('This record is read-only.')).toBeInTheDocument()
    expect(screen.getByText('A101')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument()
  })
})
