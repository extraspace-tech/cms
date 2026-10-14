import { createBody, numberText, patchBody } from './forms'

describe('patchBody', () => {
  it('sends text as typed, numbers as numbers and emptied clearable numbers in clear', () => {
    expect(patchBody({ name: 'A', displayName: '', latitude: '', taxPercentage: '7.5', sortOrder: '' },
      ['latitude', 'taxPercentage', 'sortOrder'], ['latitude']))
      .toEqual({ name: 'A', displayName: '', taxPercentage: 7.5, clear: ['latitude'] })
  })
  it('leaves clear out when nothing is cleared', () => {
    expect(patchBody({ city: 'KL' }, [], [])).toEqual({ city: 'KL' })
  })
})

describe('createBody', () => {
  it('drops empty text so the server defaults apply, and converts numbers', () => {
    expect(createBody({ name: 'A', timeZone: '', taxPercentage: '9', active: true }, ['taxPercentage']))
      .toEqual({ name: 'A', taxPercentage: 9, active: true })
  })
})

it('numberText shows a stored number as form text', () => {
  expect(numberText(null)).toBe('')
  expect(numberText(1.25)).toBe('1.25')
})
