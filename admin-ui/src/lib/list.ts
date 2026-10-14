import { useCallback } from 'react'
import { useSearchParams } from 'react-router'

/** List state lives in the address (Back and reload keep it). A new search or filter starts again at page 1. */
export function useListParams<F extends string = never>(filterNames: readonly F[] = []) {
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Math.floor(Number(params.get('page'))) || 1)
  const search = params.get('search') ?? ''
  const filters = Object.fromEntries(filterNames.map((name) => [name, params.get(name) ?? ''])) as Record<F, string>

  const update = useCallback((changes: Record<string, string | number>) => {
    setParams((current) => {
      const next = new URLSearchParams(current)
      for (const [key, value] of Object.entries(changes)) {
        if (value === '' || (key === 'page' && value === 1)) next.delete(key)
        else next.set(key, String(value))
      }
      if (!('page' in changes)) next.delete('page')
      return next
    }, { replace: true })
  }, [setParams])

  const setPage = useCallback((value: number) => update({ page: value }), [update])
  const setSearch = useCallback((value: string) => update({ search: value }), [update])
  const setFilter = useCallback((name: F, value: string) => update({ [name]: value }), [update])
  return { page, search, filters, setPage, setSearch, setFilter }
}
