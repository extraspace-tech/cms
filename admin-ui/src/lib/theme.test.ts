import { applyTheme, getTheme, setTheme, subscribeTheme, watchSystemTheme } from './theme'

/** Replaces matchMedia with one list whose `matches` the test can flip, and returns it. */
function stubSystem(dark: boolean) {
  const system = { dark, changed: () => {} }
  const addEventListener = vi.fn((_type: string, listener: () => void) => {
    system.changed = listener
  })
  vi.spyOn(window, 'matchMedia').mockImplementation((query: string) => ({
    get matches() {
      return system.dark
    },
    media: query,
    onchange: null,
    addEventListener,
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  }))
  return { system, addEventListener }
}

describe('theme', () => {
  beforeEach(() => {
    localStorage.clear()
    document.documentElement.classList.remove('dark')
  })
  afterEach(() => vi.restoreAllMocks())

  it('defaults to system and follows the system setting', () => {
    expect(getTheme()).toBe('system')
    applyTheme()
    expect(document.documentElement.classList.contains('dark')).toBe(false)   // the matchMedia stub says light
  })

  it('is dark when the choice is system and the system is dark', () => {
    stubSystem(true)
    applyTheme()
    expect(document.documentElement.classList.contains('dark')).toBe(true)
    setTheme('light')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('follows a system switch while the choice is system, with one listener', () => {
    const { system, addEventListener } = stubSystem(false)
    const subscriber = vi.fn()
    const unsubscribe = subscribeTheme(subscriber)
    watchSystemTheme()
    watchSystemTheme()
    expect(addEventListener).toHaveBeenCalledTimes(1)
    expect(addEventListener).toHaveBeenCalledWith('change', expect.any(Function))

    system.dark = true
    system.changed()
    expect(document.documentElement.classList.contains('dark')).toBe(true)
    expect(subscriber).toHaveBeenCalledTimes(1)

    setTheme('light')
    system.changed()
    expect(document.documentElement.classList.contains('dark')).toBe(false)   // an explicit choice wins over the system
    unsubscribe()
  })

  it('remembers the choice and puts the dark class on <html>', () => {
    setTheme('dark')
    expect(localStorage.getItem('es-admin-theme')).toBe('dark')
    expect(document.documentElement.classList.contains('dark')).toBe(true)
    setTheme('light')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('ignores a stored value it does not know', () => {
    localStorage.setItem('es-admin-theme', 'purple')
    expect(getTheme()).toBe('system')
  })
})
