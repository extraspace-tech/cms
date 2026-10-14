import { useParams } from 'react-router'
import { z } from 'zod'
import { ErrorNote } from '@/components/ErrorNote'
import { RecordEditor } from '@/components/RecordEditor'
import type { FieldSpec } from '@/components/RecordFields'
import { useSession } from '@/features/auth/session'
import { numberText } from '@/lib/forms'
import { atMostTwoDecimals, optionalNumber, requiredNumber } from '@/lib/zod-fields'
import { useSite, useSiteWrites, type CreateSite, type Site, type UpdateSite } from './api'

const countries = [{ value: 'SG', label: 'Singapore' }, { value: 'MY', label: 'Malaysia' }, { value: 'KR', label: 'Korea' }]
const currencies: Record<string, string> = { SG: 'SGD', MY: 'MYR', KR: 'KRW' }
const facilityTypes = [{ value: 'SelfStorage', label: 'Self Storage' }, { value: 'Workspace', label: 'Workspace' }]

// Blank on a new site = the country's value; an edit must keep one (the server refuses "").
const countryDefault = (editing: boolean, max: number) => (editing ? z.string().trim().min(1, 'Required.').max(max) : z.string().trim().max(max))

// The server validates again and is the authority; this only saves a round trip.
function schemaFor(editing: boolean) {
  return z.object({
    locationCode: z.string().trim().min(1, 'Required.').max(20, 'At most 20 characters.').regex(/^[A-Za-z0-9_-]+$/, 'Letters, digits, - and _ only.'),
    facilityType: z.enum(['SelfStorage', 'Workspace']),
    name: z.string().trim().min(1, 'Required.').max(200),
    displayName: z.string().max(200),
    description: z.string().max(4000),
    heroImageUrl: z.string().trim().max(1000).refine((v) => v === '' || v.startsWith('https://'), 'Must start with https://.'),
    address1: z.string().max(300),
    city: z.string().max(100),
    region: z.string().max(100),
    postalCode: z.string().max(20),
    country: z.enum(['SG', 'MY', 'KR'], 'Choose a country.'),
    phone: z.string().max(50),
    latitude: optionalNumber(-90, 90),
    longitude: optionalNumber(-180, 180),
    timeZone: countryDefault(editing, 64),
    taxPercentage: requiredNumber(0, 100).refine(atMostTwoDecimals, 'At most 2 decimals.'),
    taxLabel: countryDefault(editing, 20),
    currency: z.string(),
    siteLinkSiteId: optionalNumber(1, 2147483647).refine((v) => v === '' || Number.isInteger(Number(v)), 'Enter a whole number.'),
    stripeAccountId: z.string().trim().max(100).regex(/^(acct_[A-Za-z0-9]+)?$/, 'Starts with acct_.'),
    active: z.boolean(),
  })
}
type Values = z.infer<ReturnType<typeof schemaFor>>

// The demo's order (SiteLink corp code dropped: it is configuration), then our additions.
function fieldsFor(editing: boolean): FieldSpec<Values>[] {
  return [
    { name: 'locationCode', label: 'Location Code', required: true },
    { name: 'facilityType', label: 'Facility Type', kind: 'select', options: facilityTypes },
    { name: 'name', label: 'Name', required: true },
    { name: 'displayName', label: 'Display Name' },
    { name: 'description', label: 'Description', kind: 'textarea' },
    { name: 'heroImageUrl', label: 'Hero Image URL' },
    { name: 'address1', label: 'Address' },
    { name: 'city', label: 'City' },
    { name: 'region', label: 'Region' },
    { name: 'postalCode', label: 'Postal Code' },
    { name: 'country', label: 'Country', kind: 'select', options: countries, required: true },
    { name: 'phone', label: 'Phone' },
    { name: 'latitude', label: 'Latitude', kind: 'number' },
    { name: 'longitude', label: 'Longitude', kind: 'number' },
    { name: 'timeZone', label: 'Time Zone', required: editing, help: editing ? 'An IANA id, such as Asia/Singapore.' : "Empty = the country's zone." },
    { name: 'taxPercentage', label: 'Tax %', kind: 'number', required: true },
    { name: 'taxLabel', label: 'Tax Label', required: editing, help: editing ? undefined : "Empty = the country's (GST, SST or VAT)." },
    { name: 'currency', label: 'Currency', readOnly: true, derive: (v) => currencies[v.country] ?? '—', help: 'Follows the country.' },
    { name: 'siteLinkSiteId', label: 'SiteLink Site ID', kind: 'number' },
    { name: 'stripeAccountId', label: 'Stripe Account', help: 'acct_…; empty = no in-app payments at this site.' },
    { name: 'active', label: 'Active', kind: 'checkbox' },
  ]
}
const forms = { create: { schema: schemaFor(false), fields: fieldsFor(false) }, edit: { schema: schemaFor(true), fields: fieldsFor(true) } }
const numberFields = ['latitude', 'longitude', 'taxPercentage', 'siteLinkSiteId']
const clearable = ['latitude', 'longitude', 'siteLinkSiteId']

// Keys in field order: the PATCH body and its `clear` list follow it.
function values(site?: Site): Values {
  return {
    locationCode: site?.locationCode ?? '', facilityType: site?.facilityType ?? 'SelfStorage', name: site?.name ?? '',
    displayName: site?.displayName ?? '', description: site?.description ?? '', heroImageUrl: site?.heroImageUrl ?? '',
    address1: site?.address1 ?? '', city: site?.city ?? '', region: site?.region ?? '', postalCode: site?.postalCode ?? '',
    country: (site?.country ?? '') as Values['country'], phone: site?.phone ?? '',
    latitude: numberText(site?.latitude), longitude: numberText(site?.longitude), timeZone: site?.timeZone ?? '',
    taxPercentage: numberText(site?.taxPercentage), taxLabel: site?.taxLabel ?? '', currency: site?.currency ?? '—',
    siteLinkSiteId: numberText(site?.siteLinkSiteId), stripeAccountId: site?.stripeAccountId ?? '', active: site?.active ?? true,
  }
}

export function SitePage() {
  const { id } = useParams()
  const isNew = id === 'new'
  const site = useSite(isNew ? undefined : id)
  const { create, update } = useSiteWrites()
  const canWrite = useSession().can('sites.manage')
  const form = isNew ? forms.create : forms.edit
  if (!isNew && !site.data) return site.isError ? <ErrorNote error={site.error} /> : <p className="text-sm text-muted-foreground">Loading…</p>
  return (
    <RecordEditor
      key={id}
      backTo="/sites" backLabel="Sites" title={isNew ? 'New Site' : 'Site'} record={site.data}
      schema={form.schema} defaults={values(site.data)} fields={form.fields} numberFields={numberFields} clearable={clearable} canWrite={canWrite}
      create={(body) => create.mutateAsync(body as CreateSite)}
      update={(recordId, body) => update.mutateAsync({ id: recordId, body: body as UpdateSite })}
    />
  )
}
