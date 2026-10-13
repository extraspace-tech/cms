import '@/lib/zod-config'   // first: before any schema can be parsed
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter } from 'react-router'
import { App } from '@/app/App'
import { routes } from '@/app/routes'
import { makeQueryClient } from '@/lib/query'
import { applyTheme, watchSystemTheme } from '@/lib/theme'
import './index.css'

applyTheme()
watchSystemTheme()
const router = createBrowserRouter(routes, { basename: '/admin' })
const queryClient = makeQueryClient()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App router={router} queryClient={queryClient} />
  </StrictMode>,
)
