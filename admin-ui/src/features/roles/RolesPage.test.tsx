import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import type { components } from '@/api/schema'
import { DESK_ROLE_ID, OWNER_ROLE_ID, staffMe } from '@/test/data'
import { problem, TRACE } from '@/test/problems'
import { renderApp } from '@/test/render'
import { server } from '@/test/server'

type Role = components['schemas']['RoleResponse']

const owner: Role = { id: OWNER_ROLE_ID, name: 'Owner', permissions: ['staff.manage', 'roles.manage', 'audit.read', 'jobs.manage'], isSystem: true, staffCount: 1, version: 1 }
const desk: Role = { id: DESK_ROLE_ID, name: 'Front desk', permissions: ['staff.manage'], isSystem: false, staffCount: 2, version: 3 }
const catalogue = [
  { name: 'staff.manage', description: 'Add and change staff' },
  { name: 'roles.manage', description: 'Create and change roles' },
  { name: 'audit.read', description: 'Read the audit log' },
  { name: 'jobs.manage', description: 'Open the jobs dashboard' },
]

function serve(roles: Role[]) {
  const state = { roles, lists: 0 }
  server.use(
    http.get('*/admin/api/roles', () => {
      state.lists++
      return HttpResponse.json(state.roles)
    }),
    http.get('*/admin/api/permissions', () => HttpResponse.json(catalogue)),
  )
  return state
}

// Inside the table: the header shows the signed-in member's role name too.
const rowOf = (name: string) => within(within(screen.getByRole('table')).getAllByText(name)[0]!.closest('tr')!)
const closed = () => waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

async function openEdit(user: ReturnType<typeof renderApp>['user']) {
  await user.click(await screen.findByRole('button', { name: 'Edit Front desk' }))
  const dialog = within(await screen.findByRole('dialog'))
  await dialog.findByLabelText(/staff.manage/)
  return dialog
}

describe('Roles', () => {
  it('lists roles; the system role has no actions', async () => {
    serve([owner, desk])
    renderApp('/admin/roles')
    expect(await screen.findByText('Front desk')).toBeInTheDocument()
    expect(rowOf('Front desk').getByText('2')).toBeInTheDocument()
    expect(rowOf('Front desk').getByText('staff.manage')).toBeInTheDocument()
    expect(rowOf('Owner').getByText('Every permission')).toBeInTheDocument()
    expect(rowOf('Owner').queryByRole('button')).not.toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Actions' })).toBeInTheDocument()
  })

  it('shows the error, not "No roles yet.", when the list fails to load', async () => {
    server.use(http.get('*/admin/api/roles', () => problem(500, 'INTERNAL_ERROR', 'Something went wrong on our side.')))
    renderApp('/admin/roles')
    expect(await screen.findByText('Something went wrong on our side.')).toBeInTheDocument()
    expect(screen.getByText(new RegExp(TRACE))).toBeInTheDocument()
    expect(screen.queryByText('No roles yet.')).not.toBeInTheDocument()
  })

  it('creates a role with the ticked permissions', async () => {
    const state = serve([owner])
    let body: unknown
    server.use(http.post('*/admin/api/roles', async ({ request }) => {
      body = await request.json()
      state.roles = [owner, desk]
      return HttpResponse.json(desk, { status: 201 })
    }))
    const { user } = renderApp('/admin/roles')
    await user.click(await screen.findByRole('button', { name: 'Add role' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.click(await dialog.findByLabelText(/audit.read/))   // ticked first, still sent in the catalogue's order
    await user.click(dialog.getByLabelText(/staff.manage/))
    await user.type(dialog.getByLabelText('Name'), 'Front desk')
    await user.click(dialog.getByRole('button', { name: 'Add' }))
    expect(await screen.findByText('Front desk')).toBeInTheDocument()
    await closed()
    expect(body).toEqual({ name: 'Front desk', permissions: ['staff.manage', 'audit.read'] })
  })

  it('shows the server message, not the stale-record text, when a create loses a race for the name (the index answers CONFLICT)', async () => {
    serve([owner, desk])
    server.use(http.post('*/admin/api/roles', () => problem(409, 'CONFLICT', 'A record with the same value already exists.')))
    const { user } = renderApp('/admin/roles')
    await user.click(await screen.findByRole('button', { name: 'Add role' }))
    const dialog = within(await screen.findByRole('dialog'))
    await dialog.findByLabelText(/staff.manage/)
    await user.type(dialog.getByLabelText('Name'), 'front desk')
    await user.click(dialog.getByRole('button', { name: 'Add' }))
    expect(await dialog.findByText('A record with the same value already exists.')).toBeInTheDocument()
    expect(dialog.getByText(new RegExp(TRACE))).toBeInTheDocument()
    expect(dialog.queryByText(/Someone else/)).not.toBeInTheDocument()
  })

  it('puts a field error from the server on the name and ties it to the input', async () => {
    serve([owner])
    server.use(http.post('*/admin/api/roles', () =>
      problem(400, 'VALIDATION_FAILED', 'One or more fields are not valid.', { errors: { name: ['The role name cannot be blank.'] } })))
    const { user } = renderApp('/admin/roles')
    await user.click(await screen.findByRole('button', { name: 'Add role' }))
    const dialog = within(await screen.findByRole('dialog'))
    await dialog.findByLabelText(/staff.manage/)
    await user.type(dialog.getByLabelText('Name'), 'x')
    await user.click(dialog.getByRole('button', { name: 'Add' }))
    expect(await dialog.findByText('The role name cannot be blank.')).toBeInTheDocument()
    expect(dialog.getByLabelText('Name')).toBeInvalid()
    expect(dialog.getByLabelText('Name')).toHaveAccessibleDescription('The role name cannot be blank.')
  })

  it('refuses a blank name in the browser', async () => {
    serve([owner])
    const { user } = renderApp('/admin/roles')
    await user.click(await screen.findByRole('button', { name: 'Add role' }))
    const dialog = within(await screen.findByRole('dialog'))
    await dialog.findByLabelText(/staff.manage/)
    await user.click(dialog.getByRole('button', { name: 'Add' }))   // no POST handler: a request would fail the test
    expect(await dialog.findByText('Enter a name.')).toBeInTheDocument()
  })

  it('shows the error when the permission list cannot be loaded, and offers no save', async () => {
    serve([owner])
    server.use(http.get('*/admin/api/permissions', () => problem(500, 'INTERNAL_ERROR', 'Something went wrong on our side.')))
    const { user } = renderApp('/admin/roles')
    await user.click(await screen.findByRole('button', { name: 'Add role' }))
    const dialog = within(await screen.findByRole('dialog'))
    expect(await dialog.findByText('Something went wrong on our side.')).toBeInTheDocument()
    expect(dialog.getByText(new RegExp(TRACE))).toBeInTheDocument()
    expect(dialog.queryByRole('button', { name: 'Add' })).not.toBeInTheDocument()
  })

  it('edits with the row version, sends only what changed, and survives a stale save', async () => {
    const state = serve([owner, desk])
    const bodies: unknown[] = []
    server.use(http.patch(`*/admin/api/roles/${desk.id}`, async ({ request }) => {
      const sent = (await request.json()) as { version: number }
      bodies.push(sent)
      if (sent.version === 3) {
        state.roles = [owner, { ...desk, version: 4 }]   // someone else saved in between
        return problem(409, 'CONFLICT', 'The record was changed by someone else.')
      }
      return HttpResponse.json({ ...desk, permissions: ['roles.manage'], version: 5 })
    }))
    const { user } = renderApp('/admin/roles')
    const dialog = await openEdit(user)
    expect(dialog.getByLabelText('Name')).toHaveValue('Front desk')
    expect(dialog.getByLabelText(/staff.manage/)).toBeChecked()
    expect(dialog.getByLabelText(/roles.manage/)).not.toBeChecked()
    await user.click(dialog.getByLabelText(/staff.manage/))
    await user.click(dialog.getByLabelText(/roles.manage/))
    await user.click(dialog.getByRole('button', { name: 'Save' }))

    expect(await dialog.findByText(/Someone else changed this record/)).toBeInTheDocument()
    expect(dialog.getByText(new RegExp(TRACE))).toBeInTheDocument()
    expect(dialog.getByLabelText(/roles.manage/)).toBeChecked()   // what was ticked is still there
    expect(dialog.getByLabelText(/staff.manage/)).not.toBeChecked()
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    await closed()
    expect(bodies).toEqual([
      { permissions: ['roles.manage'], version: 3 },   // the untouched name is not sent
      { permissions: ['roles.manage'], version: 4 },
    ])
  })

  it('does not undo a rename someone else saved while the permissions were being edited', async () => {
    const state = serve([owner, desk])
    const bodies: unknown[] = []
    server.use(http.patch(`*/admin/api/roles/${desk.id}`, async ({ request }) => {
      const sent = (await request.json()) as { version: number }
      bodies.push(sent)
      if (sent.version === 3) {
        state.roles = [owner, { ...desk, name: 'Reception', version: 4 }]   // the other admin renamed it
        return problem(409, 'CONFLICT', 'The record was changed by someone else.')
      }
      return HttpResponse.json({ ...desk, name: 'Reception', permissions: ['audit.read', 'staff.manage'], version: 5 })
    }))
    const { user } = renderApp('/admin/roles')
    const dialog = await openEdit(user)
    await user.click(dialog.getByLabelText(/audit.read/))
    await user.click(dialog.getByRole('button', { name: 'Save' }))

    expect(await dialog.findByText(/Someone else changed this record/)).toBeInTheDocument()
    await waitFor(() => expect(dialog.getByLabelText('Name')).toHaveValue('Reception'))   // their value, shown
    expect(dialog.getByText(/Saved permissions now: staff\.manage\./)).toBeInTheDocument()
    expect(dialog.getByLabelText(/audit.read/)).toBeChecked()   // ours, kept
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    await closed()
    expect(bodies).toEqual([
      { permissions: ['staff.manage', 'audit.read'], version: 3 },
      { permissions: ['staff.manage', 'audit.read'], version: 4 },   // no name: the rename stands
    ])
  })

  it('does not undo permissions someone else saved while the name was being edited', async () => {
    const state = serve([owner, desk])
    const bodies: unknown[] = []
    server.use(http.patch(`*/admin/api/roles/${desk.id}`, async ({ request }) => {
      const sent = (await request.json()) as { version: number }
      bodies.push(sent)
      if (sent.version === 3) {
        state.roles = [owner, { ...desk, permissions: ['audit.read'], version: 4 }]
        return problem(409, 'CONFLICT', 'The record was changed by someone else.')
      }
      return HttpResponse.json({ ...desk, name: 'Reception', permissions: ['audit.read'], version: 5 })
    }))
    const { user } = renderApp('/admin/roles')
    const dialog = await openEdit(user)
    await user.clear(dialog.getByLabelText('Name'))
    await user.type(dialog.getByLabelText('Name'), 'Reception')
    await user.click(dialog.getByRole('button', { name: 'Save' }))

    expect(await dialog.findByText(/Someone else changed this record/)).toBeInTheDocument()
    await waitFor(() => expect(dialog.getByLabelText(/audit.read/)).toBeChecked())   // their boxes, shown
    expect(dialog.getByLabelText(/staff.manage/)).not.toBeChecked()
    expect(dialog.getByLabelText('Name')).toHaveValue('Reception')   // ours, kept
    expect(dialog.queryByText(/Saved permissions now/)).not.toBeInTheDocument()   // our save does not touch them
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    await closed()
    expect(bodies).toEqual([{ name: 'Reception', version: 3 }, { name: 'Reception', version: 4 }])
  })

  it('sends nothing when nothing was changed', async () => {
    const state = serve([owner, desk])
    const { user } = renderApp('/admin/roles')
    const dialog = await openEdit(user)
    await user.click(dialog.getByLabelText(/audit.read/))   // ticked and unticked again: the same choice
    await user.click(dialog.getByLabelText(/audit.read/))
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    await closed()
    expect(state.lists).toBe(1)   // no PATCH (it has no handler here) and no reload
  })

  it('refreshes the signed-in staff member after their own role is renamed', async () => {
    const state = serve([owner, desk])
    const me = { ...staffMe, permissions: ['staff.manage', 'roles.manage'] }
    server.use(http.patch(`*/admin/api/roles/${desk.id}`, () => {
      state.roles = [owner, { ...desk, name: 'Reception', version: 4 }]
      server.use(http.get('*/admin/api/me', () => HttpResponse.json({ ...me, roleName: 'Reception' })))
      return HttpResponse.json(state.roles[1])
    }))
    const { user } = renderApp('/admin/roles', { me })
    const dialog = await openEdit(user)
    await user.clear(dialog.getByLabelText('Name'))
    await user.type(dialog.getByLabelText('Name'), 'Reception')
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    await closed()
    await waitFor(() => expect(screen.getAllByText('Reception')).toHaveLength(2))   // the row and the header
    expect(screen.queryByText('Front desk')).not.toBeInTheDocument()
  })

  it('deletes with the version in the query, and shows why a role in use cannot go', async () => {
    serve([owner, desk])
    let query = ''
    server.use(http.delete(`*/admin/api/roles/${desk.id}`, ({ request }) => {
      query = new URL(request.url).search
      return problem(409, 'ROLE_IN_USE', 'Staff still hold this role. Move them to another role first.')
    }))
    const { user } = renderApp('/admin/roles')
    await user.click(await screen.findByRole('button', { name: 'Delete Front desk' }))
    const dialog = within(await screen.findByRole('dialog'))
    expect(dialog.getByText('Delete the role "Front desk"? This cannot be undone.')).toBeInTheDocument()
    await user.click(dialog.getByRole('button', { name: 'Delete' }))
    expect(await dialog.findByText('Staff still hold this role. Move them to another role first.')).toBeInTheDocument()
    expect(dialog.getByText(new RegExp(TRACE))).toBeInTheDocument()
    expect(query).toBe('?version=3')
  })

  it('removes the row after a delete', async () => {
    const state = serve([owner, desk])
    server.use(http.delete(`*/admin/api/roles/${desk.id}`, () => {
      state.roles = [owner]
      return new HttpResponse(null, { status: 204 })
    }))
    const { user } = renderApp('/admin/roles')
    await user.click(await screen.findByRole('button', { name: 'Delete Front desk' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Delete' }))
    await closed()
    expect(screen.queryByText('Front desk')).not.toBeInTheDocument()
  })

  it('keeps the name the delete was asked for when someone else renamed the role, and retries with the new version', async () => {
    const state = serve([owner, desk])
    const queries: string[] = []
    server.use(http.delete(`*/admin/api/roles/${desk.id}`, ({ request }) => {
      queries.push(new URL(request.url).search)
      if (queries.length === 1) {
        state.roles = [owner, { ...desk, name: 'Reception', version: 4 }]
        return problem(409, 'CONFLICT', 'The record was changed by someone else.')
      }
      state.roles = [owner]
      return new HttpResponse(null, { status: 204 })
    }))
    const { user } = renderApp('/admin/roles')
    await user.click(await screen.findByRole('button', { name: 'Delete Front desk' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.click(dialog.getByRole('button', { name: 'Delete' }))
    expect(await dialog.findByText('The record was changed by someone else.')).toBeInTheDocument()
    expect(dialog.getByText('Delete the role "Front desk"? Someone else has renamed it to "Reception". This cannot be undone.')).toBeInTheDocument()
    await user.click(dialog.getByRole('button', { name: 'Delete' }))
    await closed()
    expect(queries).toEqual(['?version=3', '?version=4'])
  })

  it('closes the delete dialog when someone else already deleted the role', async () => {
    const state = serve([owner, desk])
    server.use(http.delete(`*/admin/api/roles/${desk.id}`, () => {
      state.roles = [owner]
      return problem(404, 'NOT_FOUND', 'Role not found.')
    }))
    const { user } = renderApp('/admin/roles')
    await user.click(await screen.findByRole('button', { name: 'Delete Front desk' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Delete' }))
    await closed()
    expect(screen.queryByText('Front desk')).not.toBeInTheDocument()
    expect(await screen.findByText('Role not found.')).toBeInTheDocument()   // a toast, with the trace id
    expect(screen.getByText(`Trace ${TRACE}`)).toBeInTheDocument()

    // The old failure is not carried into the next dialog.
    await user.click(screen.getByRole('button', { name: 'Add role' }))
    expect(within(await screen.findByRole('dialog')).queryByText('Role not found.')).not.toBeInTheDocument()
  })

  it('closes the edit dialog when someone else deleted the role', async () => {
    const state = serve([owner, desk])
    server.use(http.patch(`*/admin/api/roles/${desk.id}`, () => {
      state.roles = [owner]
      return problem(404, 'NOT_FOUND', 'Role not found.')
    }))
    const { user } = renderApp('/admin/roles')
    const dialog = await openEdit(user)
    await user.click(dialog.getByLabelText(/audit.read/))
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    await closed()
    expect(await screen.findByText('Role not found.')).toBeInTheDocument()
    expect(screen.queryByText('Front desk')).not.toBeInTheDocument()
  })

  it('shows what is saved now when both sides changed the permissions, and the retry sends this user\'s list', async () => {
    const state = serve([owner, desk])
    const bodies: unknown[] = []
    server.use(http.patch(`*/admin/api/roles/${desk.id}`, async ({ request }) => {
      const sent = (await request.json()) as { version: number }
      bodies.push(sent)
      if (sent.version === 3) {
        state.roles = [owner, { ...desk, permissions: ['roles.manage'], version: 4 }]   // the other admin took staff.manage away
        return problem(409, 'CONFLICT', 'The record was changed by someone else.')
      }
      return HttpResponse.json({ ...desk, permissions: ['audit.read', 'staff.manage'], version: 5 })
    }))
    const { user } = renderApp('/admin/roles')
    const dialog = await openEdit(user)
    await user.click(dialog.getByLabelText(/audit.read/))
    await user.click(dialog.getByRole('button', { name: 'Save' }))

    expect(await dialog.findByText(/Someone else changed this record/)).toBeInTheDocument()
    expect(await dialog.findByText(/Saved permissions now: roles\.manage\./)).toBeInTheDocument()   // theirs, in view
    expect(dialog.getByLabelText(/staff.manage/)).toBeChecked()   // ours, still ticked
    expect(dialog.getByLabelText(/audit.read/)).toBeChecked()
    expect(dialog.getByLabelText(/roles.manage/)).not.toBeChecked()
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    await closed()
    expect(bodies).toEqual([
      { permissions: ['staff.manage', 'audit.read'], version: 3 },
      { permissions: ['staff.manage', 'audit.read'], version: 4 },
    ])
  })

  it('says "none" when the other admin removed every permission', async () => {
    const state = serve([owner, desk])
    server.use(http.patch(`*/admin/api/roles/${desk.id}`, () => {
      state.roles = [owner, { ...desk, permissions: [], version: 4 }]
      return problem(409, 'CONFLICT', 'The record was changed by someone else.')
    }))
    const { user } = renderApp('/admin/roles')
    const dialog = await openEdit(user)
    await user.click(dialog.getByLabelText(/audit.read/))
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    expect(await dialog.findByText(/Saved permissions now: none\./)).toBeInTheDocument()
  })

  it('puts a taken name on the name field when adding', async () => {
    serve([owner, desk])
    server.use(http.post('*/admin/api/roles', () => problem(409, 'ROLE_NAME_TAKEN', 'A role with this name already exists.')))
    const { user } = renderApp('/admin/roles')
    await user.click(await screen.findByRole('button', { name: 'Add role' }))
    const dialog = within(await screen.findByRole('dialog'))
    await dialog.findByLabelText(/staff.manage/)
    await user.type(dialog.getByLabelText('Name'), 'front desk')
    await user.click(dialog.getByRole('button', { name: 'Add' }))
    expect(await dialog.findByText('A role with this name already exists.')).toBeInTheDocument()
    expect(dialog.getByLabelText('Name')).toBeInvalid()
    expect(dialog.getByLabelText('Name')).toHaveAccessibleDescription('A role with this name already exists.')
    expect(dialog.queryByText(/Someone else/)).not.toBeInTheDocument()
  })

  it('puts a taken name on the name field when renaming, without the stale-record text', async () => {
    const taken: Role = { id: '0199dd00-0000-7000-8000-0000000000bb', name: 'Reception', permissions: [], isSystem: false, staffCount: 0, version: 2 }
    serve([owner, desk, taken])
    const bodies: unknown[] = []
    server.use(http.patch(`*/admin/api/roles/${desk.id}`, async ({ request }) => {
      bodies.push(await request.json())
      return problem(409, 'ROLE_NAME_TAKEN', 'A role with this name already exists.')
    }))
    const { user } = renderApp('/admin/roles')
    const dialog = await openEdit(user)
    await user.clear(dialog.getByLabelText('Name'))
    await user.type(dialog.getByLabelText('Name'), 'reception')
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    expect(await dialog.findByText('A role with this name already exists.')).toBeInTheDocument()
    expect(dialog.getByLabelText('Name')).toBeInvalid()
    expect(dialog.getByLabelText('Name')).toHaveValue('reception')
    expect(dialog.queryByText(/Someone else/)).not.toBeInTheDocument()
    expect(bodies).toEqual([{ name: 'reception', version: 3 }])
  })

  it('keeps the edit dialog open with the error when the role is gone and the reload fails too', async () => {
    serve([owner, desk])
    server.use(http.patch(`*/admin/api/roles/${desk.id}`, () => {
      server.use(http.get('*/admin/api/roles', () => problem(500, 'INTERNAL_ERROR', 'Something went wrong on our side.')))
      return problem(404, 'NOT_FOUND', 'Role not found.')
    }))
    const { user } = renderApp('/admin/roles')
    const dialog = await openEdit(user)
    await user.click(dialog.getByLabelText(/audit.read/))
    await user.click(dialog.getByRole('button', { name: 'Save' }))
    expect(await dialog.findByText('Role not found.')).toBeInTheDocument()   // inline, not only the toast
    expect(dialog.getByText(new RegExp(TRACE))).toBeInTheDocument()
    expect(dialog.getByLabelText(/audit.read/)).toBeChecked()
  })
})
