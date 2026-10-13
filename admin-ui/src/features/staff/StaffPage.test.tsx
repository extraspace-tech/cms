import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import type { components } from '@/api/schema'
import { DESK_ROLE_ID, OWNER_ROLE_ID, ownerMe, staffMe } from '@/test/data'
import { problem, TRACE } from '@/test/problems'
import { renderApp } from '@/test/render'
import { server } from '@/test/server'

type Staff = components['schemas']['StaffResponse']

const roles = [
  { id: OWNER_ROLE_ID, name: 'Owner', permissions: [], isSystem: true, staffCount: 1, version: 1 },
  { id: DESK_ROLE_ID, name: 'Front desk', permissions: ['staff.manage'], isSystem: false, staffCount: 1, version: 3 },
]
const olive: Staff = { id: ownerMe.id, email: ownerMe.email, displayName: 'Olive Owner', roleId: OWNER_ROLE_ID, roleName: 'Owner', active: true, lastSignInAt: '2026-10-13T02:30:00Z', version: 11 }
const dan: Staff = { id: staffMe.id, email: staffMe.email, displayName: null, roleId: DESK_ROLE_ID, roleName: 'Front desk', active: true, lastSignInAt: null, version: 5 }

function serve(staff: Staff[]) {
  const state = { staff, lists: 0 }
  server.use(
    http.get('*/admin/api/roles', () => HttpResponse.json(roles)),
    http.get('*/admin/api/staff', () => {
      state.lists++
      return HttpResponse.json(state.staff)
    }),
  )
  return state
}

const rowOf = (email: string) => within(screen.getByText(email).closest('tr')!)

describe('Staff', () => {
  it('lists staff with role, status and last sign-in', async () => {
    serve([olive, dan])
    renderApp('/admin/staff')
    expect(await screen.findByText('desk@example.test')).toBeInTheDocument()
    expect(rowOf('owner@example.test').getByText('Olive Owner')).toBeInTheDocument()
    expect(rowOf('owner@example.test').getByText('Active')).toBeInTheDocument()
    expect(rowOf('desk@example.test').getByText('Front desk')).toBeInTheDocument()
    expect(rowOf('desk@example.test').getByText('Never')).toBeInTheDocument()
  })

  it('adds a staff member', async () => {
    const state = serve([olive])
    let body: unknown
    server.use(http.post('*/admin/api/staff', async ({ request }) => {
      body = await request.json()
      state.staff = [olive, dan]
      return HttpResponse.json(dan, { status: 201 })
    }))
    const { user } = renderApp('/admin/staff')
    await user.click(await screen.findByRole('button', { name: 'Add staff' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Email'), 'desk@example.test')
    await user.type(dialog.getByLabelText('Display name'), 'Dan Desk')
    await user.selectOptions(dialog.getByLabelText('Role'), DESK_ROLE_ID)
    await user.click(dialog.getByRole('button', { name: 'Add' }))

    expect(await screen.findByText('desk@example.test')).toBeInTheDocument()
    expect(body).toEqual({ email: 'desk@example.test', displayName: 'Dan Desk', roleId: DESK_ROLE_ID })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('shows a field error from the server on its field', async () => {
    serve([olive])
    server.use(http.post('*/admin/api/staff', () =>
      problem(400, 'VALIDATION_FAILED', 'One or more fields are not valid.', { errors: { email: ['The Email field is not a valid e-mail address.'] } })))
    const { user } = renderApp('/admin/staff')
    await user.click(await screen.findByRole('button', { name: 'Add staff' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Email'), 'new@example.test')   // passes the browser check: the refusal is the server's
    await user.selectOptions(dialog.getByLabelText('Role'), DESK_ROLE_ID)
    await user.click(dialog.getByRole('button', { name: 'Add' }))
    expect(await dialog.findByText('The Email field is not a valid e-mail address.')).toBeInTheDocument()
  })

  it('edits with the row version and sends only what changed', async () => {
    const state = serve([olive, dan])
    let body: unknown
    server.use(http.patch(`*/admin/api/staff/${dan.id}`, async ({ request }) => {
      body = await request.json()
      state.staff = [olive, { ...dan, displayName: 'Dan Desk', version: 6 }]
      return HttpResponse.json(state.staff[1])
    }))
    const { user } = renderApp('/admin/staff')
    await screen.findByText('desk@example.test')
    await user.click(rowOf('desk@example.test').getByRole('button', { name: 'Edit desk@example.test' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Display name'), 'Dan Desk')
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    expect(await screen.findByText('Dan Desk')).toBeInTheDocument()
    expect(body).toEqual({ displayName: 'Dan Desk', version: 5 })   // the untouched role is not sent
  })

  it('reloads the list when the save is stale', async () => {
    const state = serve([olive, dan])
    const bodies: unknown[] = []
    server.use(http.patch(`*/admin/api/staff/${dan.id}`, async ({ request }) => {
      const sent = (await request.json()) as { version: number }
      bodies.push(sent)
      if (sent.version === 5) {
        state.staff = [olive, { ...dan, version: 9 }]   // someone else saved in between
        return problem(409, 'CONFLICT', 'The record was changed by someone else.')
      }
      return HttpResponse.json({ ...dan, version: 10 })
    }))
    const { user } = renderApp('/admin/staff')
    await screen.findByText('desk@example.test')
    await user.click(rowOf('desk@example.test').getByRole('button', { name: 'Edit desk@example.test' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Display name'), 'Dan')
    await user.click(dialog.getByRole('button', { name: 'Save' }))

    expect(await dialog.findByText(/Someone else changed this record/)).toBeInTheDocument()
    expect(dialog.getByText(new RegExp(TRACE))).toBeInTheDocument()
    expect(dialog.getByLabelText('Display name')).toHaveValue('Dan')   // what was typed is still there
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(bodies).toEqual([{ displayName: 'Dan', version: 5 }, { displayName: 'Dan', version: 9 }])
  })

  it('deactivates after a confirmation and shows a business refusal', async () => {
    serve([olive, dan])
    const bodies: unknown[] = []
    server.use(http.patch(`*/admin/api/staff/${dan.id}`, async ({ request }) => {
      bodies.push(await request.json())
      return problem(409, 'LAST_OWNER', 'At least one active Owner must remain.')
    }))
    const { user } = renderApp('/admin/staff')
    await screen.findByText('desk@example.test')
    await user.click(rowOf('desk@example.test').getByRole('button', { name: 'Deactivate desk@example.test' }))
    const dialog = within(await screen.findByRole('dialog'))
    expect(dialog.getByText(/desk@example.test will be signed out/)).toBeInTheDocument()
    await user.click(dialog.getByRole('button', { name: 'Deactivate' }))
    expect(await dialog.findByText('At least one active Owner must remain.')).toBeInTheDocument()
    expect(bodies).toEqual([{ active: false, version: 5 }])
  })

  it('offers no way to deactivate yourself', async () => {
    serve([olive, dan])
    renderApp('/admin/staff')
    await screen.findByText('desk@example.test')
    expect(rowOf('owner@example.test').queryByRole('button', { name: /Deactivate/ })).not.toBeInTheDocument()
    expect(rowOf('owner@example.test').getByRole('button', { name: 'Edit owner@example.test' })).toBeInTheDocument()
  })

  it('does not offer the Owner role to a non-Owner', async () => {
    serve([olive, dan])
    const { user } = renderApp('/admin/staff', { me: staffMe })
    await screen.findByRole('button', { name: 'Edit desk@example.test' })   // the roles are loaded
    expect(rowOf('owner@example.test').queryByRole('button')).not.toBeInTheDocument()   // an Owner's row is Owner-only
    await user.click(screen.getByRole('button', { name: 'Add staff' }))
    const dialog = within(await screen.findByRole('dialog'))
    const options = within(dialog.getByLabelText('Role')).getAllByRole('option').map((o) => o.textContent)
    expect(options).toEqual(['Choose a role', 'Front desk'])
  })

  it('reactivates an inactive staff member', async () => {
    serve([olive, { ...dan, active: false }])
    let body: unknown
    server.use(http.patch(`*/admin/api/staff/${dan.id}`, async ({ request }) => {
      body = await request.json()
      return HttpResponse.json({ ...dan, version: 6 })
    }))
    const { user } = renderApp('/admin/staff')
    await screen.findByText('desk@example.test')
    expect(rowOf('desk@example.test').getByText('Inactive')).toBeInTheDocument()
    await user.click(rowOf('desk@example.test').getByRole('button', { name: 'Reactivate desk@example.test' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Reactivate' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(body).toEqual({ active: true, version: 5 })
  })

  it('does not undo a change someone else saved to a field the user left alone', async () => {
    const state = serve([olive, dan])
    const bodies: unknown[] = []
    server.use(http.patch(`*/admin/api/staff/${dan.id}`, async ({ request }) => {
      const sent = (await request.json()) as { version: number }
      bodies.push(sent)
      if (sent.version === 5) {
        state.staff = [olive, { ...dan, displayName: 'Daniel', version: 9 }]   // someone else renamed him in between
        return problem(409, 'CONFLICT', 'The record was changed by someone else.')
      }
      return HttpResponse.json({ ...dan, displayName: 'Daniel', roleId: OWNER_ROLE_ID, roleName: 'Owner', version: 10 })
    }))
    const { user } = renderApp('/admin/staff')
    await screen.findByText('desk@example.test')
    await user.click(rowOf('desk@example.test').getByRole('button', { name: 'Edit desk@example.test' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.selectOptions(dialog.getByLabelText('Role'), OWNER_ROLE_ID)
    await user.click(dialog.getByRole('button', { name: 'Save' }))

    expect(await dialog.findByText(/Someone else changed this record/)).toBeInTheDocument()
    await waitFor(() => expect(dialog.getByLabelText('Display name')).toHaveValue('Daniel'))   // their value, shown
    expect(dialog.getByLabelText('Role')).toHaveValue(OWNER_ROLE_ID)   // ours, kept
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(bodies).toEqual([{ roleId: OWNER_ROLE_ID, version: 5 }, { roleId: OWNER_ROLE_ID, version: 9 }])
  })

  it('sends nothing when nothing was changed', async () => {
    const state = serve([olive, dan])
    const { user } = renderApp('/admin/staff')
    await screen.findByText('desk@example.test')
    await user.click(rowOf('desk@example.test').getByRole('button', { name: 'Edit desk@example.test' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(state.lists).toBe(1)   // no PATCH (it has no handler here) and no reload
  })

  it('saves an edit of a member whose stored email the browser check would refuse', async () => {
    const odd = { ...dan, email: 'desk@localhost' }
    serve([olive, odd])
    let body: unknown
    server.use(http.patch(`*/admin/api/staff/${dan.id}`, async ({ request }) => {
      body = await request.json()
      return HttpResponse.json({ ...odd, displayName: 'Dan', version: 6 })
    }))
    const { user } = renderApp('/admin/staff')
    await user.click(await screen.findByRole('button', { name: 'Edit desk@localhost' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Display name'), 'Dan')
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(body).toEqual({ displayName: 'Dan', version: 5 })
  })

  it('shows the server message, not the stale-record text, when the added email is taken', async () => {
    serve([olive])
    server.use(http.post('*/admin/api/staff', () => problem(409, 'CONFLICT', 'A record with the same value already exists.')))
    const { user } = renderApp('/admin/staff')
    await user.click(await screen.findByRole('button', { name: 'Add staff' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Email'), 'owner@example.test')
    await user.selectOptions(dialog.getByLabelText('Role'), DESK_ROLE_ID)
    await user.click(dialog.getByRole('button', { name: 'Add' }))
    expect(await dialog.findByText('A record with the same value already exists.')).toBeInTheDocument()
    expect(dialog.getByText(new RegExp(TRACE))).toBeInTheDocument()
    expect(dialog.queryByText(/Someone else/)).not.toBeInTheDocument()
  })

  it('ties a field error to its input', async () => {
    serve([olive])
    const { user } = renderApp('/admin/staff')
    await user.click(await screen.findByRole('button', { name: 'Add staff' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Email'), 'new@example.test')
    await user.click(dialog.getByRole('button', { name: 'Add' }))
    expect(await dialog.findByText('Choose a role.')).toBeInTheDocument()
    expect(dialog.getByLabelText('Role')).toBeInvalid()
    expect(dialog.getByLabelText('Role')).toHaveAccessibleDescription('Choose a role.')
    expect(dialog.getByLabelText('Email')).toBeValid()
  })

  it('does not turn a deactivation into a reactivation when someone else deactivated first', async () => {
    const state = serve([olive, dan])
    const bodies: unknown[] = []
    server.use(http.patch(`*/admin/api/staff/${dan.id}`, async ({ request }) => {
      bodies.push(await request.json())
      state.staff = [olive, { ...dan, active: false, version: 9 }]   // the other admin won
      return problem(409, 'CONFLICT', 'The record was changed by someone else.')
    }))
    const { user } = renderApp('/admin/staff')
    await screen.findByText('desk@example.test')
    await user.click(rowOf('desk@example.test').getByRole('button', { name: 'Deactivate desk@example.test' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.click(dialog.getByRole('button', { name: 'Deactivate' }))

    expect(await dialog.findByText('desk@example.test is already inactive.')).toBeInTheDocument()
    expect(dialog.getByText('Deactivate staff member')).toBeInTheDocument()
    expect(dialog.getByText(new RegExp(TRACE))).toBeInTheDocument()
    expect(dialog.queryByRole('button', { name: /activate/i })).not.toBeInTheDocument()   // nothing left to send
    await user.click(dialog.getAllByRole('button', { name: 'Close' })[0])
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(bodies).toEqual([{ active: false, version: 5 }])
  })

  it('refreshes the signed-in staff member after they edit themself', async () => {
    const state = serve([olive, dan])
    server.use(http.patch(`*/admin/api/staff/${olive.id}`, () => {
      state.staff = [{ ...olive, displayName: 'Olive O', version: 12 }, dan]
      server.use(http.get('*/admin/api/me', () => HttpResponse.json({ ...ownerMe, displayName: 'Olive O' })))
      return HttpResponse.json(state.staff[0])
    }))
    const { user } = renderApp('/admin/staff')
    await user.click(await screen.findByRole('button', { name: 'Edit owner@example.test' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.clear(dialog.getByLabelText('Display name'))
    await user.type(dialog.getByLabelText('Display name'), 'Olive O')
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getAllByText('Olive O')).toHaveLength(2)   // the row and the header
  })

  it('offers a non-Owner no row actions until the roles are loaded', async () => {
    let release = () => {}
    const held = new Promise<void>((resolve) => { release = resolve })
    serve([olive, dan])
    server.use(http.get('*/admin/api/roles', async () => {
      await held
      return HttpResponse.json(roles)
    }))
    renderApp('/admin/staff', { me: staffMe })
    await screen.findByText('owner@example.test')
    expect(screen.queryByRole('button', { name: /^(Edit|Deactivate) / })).not.toBeInTheDocument()
    release()
    expect(await screen.findByRole('button', { name: 'Edit desk@example.test' })).toBeInTheDocument()
    expect(rowOf('owner@example.test').queryByRole('button')).not.toBeInTheDocument()
  })

  it('shows the error, not "No staff yet.", when the list fails to load', async () => {
    server.use(
      http.get('*/admin/api/roles', () => HttpResponse.json(roles)),
      http.get('*/admin/api/staff', () => problem(500, 'INTERNAL_ERROR', 'Something went wrong on our side.')),
    )
    renderApp('/admin/staff')
    expect(await screen.findByText('Something went wrong on our side.')).toBeInTheDocument()
    expect(screen.queryByText('No staff yet.')).not.toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Actions' })).toBeInTheDocument()
  })
})
