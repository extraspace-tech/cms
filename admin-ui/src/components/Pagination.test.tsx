import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Pagination } from './Pagination'

it('shows start–end of total and pages forward', async () => {
  const onPage = vi.fn()
  render(<Pagination page={2} pageSize={20} total={45} onPage={onPage} />)
  expect(screen.getByText('21–40 of 45')).toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: 'Next' }))
  expect(onPage).toHaveBeenCalledWith(3)
})

it('disables Next on the last page', () => {
  render(<Pagination page={3} pageSize={20} total={45} onPage={() => {}} />)
  expect(screen.getByText('41–45 of 45')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
})
