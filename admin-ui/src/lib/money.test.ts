import { formatMoney } from './money'

describe('formatMoney', () => {
  it('uses the currency places: cents for SGD and MYR, none for KRW', () => {
    expect(formatMoney(1234.5, 'SGD')).toMatch(/1,234\.50$/)
    expect(formatMoney(99, 'MYR')).toMatch(/99\.00$/)
    expect(formatMoney(1234.5, 'KRW')).toMatch(/1,235$/)
  })
  it('shows a dash for no amount', () => {
    expect(formatMoney(null, 'SGD')).toBe('—')
  })
})
