const places: Record<string, number> = { SGD: 2, MYR: 2, KRW: 0 }

/** An amount in its own currency, with that currency's places (KRW has none); "—" when there is none. */
export function formatMoney(amount: number | null | undefined, currency: string): string {
  if (amount === null || amount === undefined) return '—'
  const digits = places[currency] ?? 2
  return new Intl.NumberFormat('en-SG', { style: 'currency', currency, minimumFractionDigits: digits, maximumFractionDigits: digits }).format(amount)
}
