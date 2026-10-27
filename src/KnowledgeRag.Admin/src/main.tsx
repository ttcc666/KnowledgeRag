import { StrictMode } from 'react'
import ReactDOM from 'react-dom/client'
import { QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from '@tanstack/react-router'
import { AxiosError } from 'axios'
import { Toaster, toast } from 'sonner'
import { router } from './router'
import { useAuthStore } from './stores/auth-store'
import './styles/index.css'

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 10_000, retry: (count, error) => !(error instanceof AxiosError && [401, 403, 404].includes(error.response?.status ?? 0)) && count < 2 } },
  queryCache: new QueryCache({ onError: (error) => { if (error instanceof AxiosError && error.response?.status === 403) toast.error('没有执行该操作的权限') } }),
})

if (localStorage.getItem('theme') === 'dark') document.documentElement.classList.add('dark')
const root = document.getElementById('root')!
ReactDOM.createRoot(root).render(<StrictMode><QueryClientProvider client={queryClient}><RouterProvider router={router} /><Toaster richColors position='top-right' /></QueryClientProvider></StrictMode>)

window.addEventListener('storage', (event) => { if (event.key === 'knowledge-rag-admin-session' && !event.newValue) { useAuthStore.getState().reset(); window.location.assign('/sign-in') } })
