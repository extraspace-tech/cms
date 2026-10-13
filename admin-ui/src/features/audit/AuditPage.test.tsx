import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { DESK_ROLE_ID, ownerMe, staffMe } from '@/test/data'
import { localInputToInstant } from '@/lib/time'
import { problem, TRACE } from '@/test/problems'
import { renderApp } from '@/test/render'
import { server } from '@/test/server'

const item = {
  id: '0199dd00-0000-7000-8000-000000000901', occurredAt: '2026-10-13T02:30:00Z', staffId: ownerMe.id, staffEmail: ownerMe.email,
  action: 'PATCH /admin/api/staff/{id:guid}', entityType: 'staff_user', entityId: staffMe.id,
  changes: { active: { from: true, to: false }, roleId: { from: null, to: DESK_ROLE_ID } }, ip: '203.0.113.9',
}

function serve(total: number, items: unknown[] = [item]) {
  const queries: URLSearchParams[] = []
  server.use(
    http.get('*/admin/api/staff', () => HttpResponse.json([
      { id: ownerMe.id, email: ownerMe.email, displayName: null, roleId: ownerMe.roleId, roleName: 'Owner', active: true, lastSignInAt: null, version: 1 },
    ])),
    http.get('*/admin/api/audit', ({ request }) => {
      const query = new URL(request.url).searchParams
      queries.push(query)
      return HttpResponse.json({ items, total, page: Number(query.get('page') ?? 1), pageSize: 50 })
    }),
  )
  return queries
}

describe('Audit log', () => {
  it('shows who changed what, with each field before and after', async () => {
    const queries = serve(1)
    renderApp('/admin/audit')
    expect(await screen.findByText('PATCH /admin/api/staff/{id:guid}')).toBeInTheDocument()
    const table = within(screen.getByRole('table'))   // the staff filter lists the same email
    expect(table.getByText('owner@example.test')).toBeInTheDocument()
    expect(table.getByText('staff_user')).toBeInTheDocument()
    expect(table.getByText('active')).toBeInTheDocument()
    expect(table.getByText('true → false')).toBeInTheDocument()
    expect(table.getByText(`— → ${DESK_ROLE_ID}`)).toBeInTheDocument()
    expect(screen.getByText('1 entry')).toBeInTheDocument()
    expect(queries[0]!.toString()).toBe('page=1&pageSize=50')
  })

  it('sends the filters only on Apply; times go out as instants with Z', async () => {
    const queries = serve(1)
    const { user } = renderApp('/admin/audit')
    await screen.findByText('PATCH /admin/api/staff/{id:guid}')
    await screen.findByRole('option', { name: 'owner@example.test' })
    await user.selectOptions(screen.getByLabelText('Staff'), ownerMe.id)
    await user.selectOptions(screen.getByLabelText('Record type'), 'staff_user')
    await user.type(screen.getByLabelText('From'), '2026-10-13T08:00')
    await user.type(screen.getByLabelText('To'), '2026-10-14T08:00')
    expect(queries).toHaveLength(1)   // nothing was asked while choosing
    await user.click(screen.getByRole('button', { name: 'Apply' }))
    await vi.waitFor(() => expect(queries).toHaveLength(2))
    await screen.findByText('PATCH /admin/api/staff/{id:guid}')

    const last = queries.at(-1)!
    expect(last.get('staffId')).toBe(ownerMe.id)
    expect(last.get('entityType')).toBe('staff_user')
    expect(last.get('from')).toBe(localInputToInstant('2026-10-13T08:00'))
    expect(last.get('from')).toMatch(/Z$/)
    expect(last.get('to')).toBe(localInputToInstant('2026-10-14T08:00'))
    expect(last.get('to')).toMatch(/Z$/)
    expect(last.get('page')).toBe('1')
  })

  it('pages through the results and goes back to page 1 when the filter changes', async () => {
    const queries = serve(120)
    const { user } = renderApp('/admin/audit')
    expect(await screen.findByText('Page 1 of 3')).toBeInTheDocument()
    expect(screen.getByText('120 entries')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Next' }))
    expect(await screen.findByText('Page 2 of 3')).toBeInTheDocument()
    await vi.waitFor(() => expect(queries.at(-1)!.get('page')).toBe('2'))

    await user.selectOptions(screen.getByLabelText('Record type'), 'role')
    await user.click(screen.getByRole('button', { name: 'Apply' }))
    expect(await screen.findByText('Page 1 of 3')).toBeInTheDocument()
    await vi.waitFor(() => expect(queries.at(-1)!.get('entityType')).toBe('role'))
    expect(queries.at(-1)!.get('page')).toBe('1')
  })

  it('has no staff filter without staff.manage (that list would be refused)', async () => {
    serve(1)
    renderApp('/admin/audit', { me: { ...staffMe, permissions: ['audit.read'] } })
    await screen.findByText('PATCH /admin/api/staff/{id:guid}')
    expect(screen.queryByLabelText('Staff')).not.toBeInTheDocument()
  })

  it('says so when nothing matches', async () => {
    serve(0, [])
    renderApp('/admin/audit')
    expect(await screen.findByText('No entries match.')).toBeInTheDocument()
    expect(screen.getByText('0 entries')).toBeInTheDocument()
    expect(screen.getByText('Page 1 of 1')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
  })

  it('shows changes of another shape as raw JSON', async () => {
    serve(1, [{ ...item, changes: ['created'] }])
    renderApp('/admin/audit')
    expect(await screen.findByText('["created"]')).toBeInTheDocument()
  })

  it('shows the error with its trace id, not "No entries match.", when the search is refused', async () => {
    server.use(
      http.get('*/admin/api/staff', () => HttpResponse.json([])),
      http.get('*/admin/api/audit', () => problem(400, 'VALIDATION_FAILED', "'from' must not be after 'to'.")),
    )
    renderApp('/admin/audit')
    expect(await screen.findByText("'from' must not be after 'to'.")).toBeInTheDocument()
    expect(screen.getByText(new RegExp(TRACE))).toBeInTheDocument()
    expect(screen.queryByText('No entries match.')).not.toBeInTheDocument()
    expect(screen.queryByText(/entries/)).not.toBeInTheDocument()
  })

  it('offers no paging while a page is loading, and says so when a page is beyond the end', async () => {
    let release = () => {}
    const held = new Promise<void>((resolve) => { release = resolve })
    server.use(
      http.get('*/admin/api/staff', () => HttpResponse.json([])),
      http.get('*/admin/api/audit', async ({ request }) => {
        const page = Number(new URL(request.url).searchParams.get('page'))
        if (page === 1) return HttpResponse.json({ items: [item], total: 120, page, pageSize: 50 })
        await held
        return HttpResponse.json({ items: [], total: 120, page, pageSize: 50 })   // the rows went away meanwhile
      }),
    )
    const { user } = renderApp('/admin/audit')
    expect(await screen.findByText('Page 1 of 3')).toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Next' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Loading…')
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()   // the total on screen is the old one
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled()
    release()
    expect(await screen.findByText('No entries on this page.')).toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Previous' })).toBeEnabled()
    expect(screen.queryByText('No entries match.')).not.toBeInTheDocument()
  })

  it('shows the error when the staff list for the filter cannot be loaded', async () => {
    serve(1)
    server.use(http.get('*/admin/api/staff', () => problem(500, 'INTERNAL_ERROR', 'Something went wrong on our side.')))
    renderApp('/admin/audit')
    expect(await screen.findByText('Something went wrong on our side.')).toBeInTheDocument()
    expect(screen.getByText(new RegExp(TRACE))).toBeInTheDocument()
    expect(await screen.findByText('PATCH /admin/api/staff/{id:guid}')).toBeInTheDocument()   // the log itself still works
  })

  it('shows a dash for an entry without changes', async () => {
    serve(1, [{ ...item, changes: null }])
    renderApp('/admin/audit')
    await screen.findByText('PATCH /admin/api/staff/{id:guid}')
    expect(within(screen.getByRole('table')).getByText('—')).toBeInTheDocument()
    expect(screen.queryByText('null')).not.toBeInTheDocument()
  })
})
