import { lazy, Suspense, type ComponentType } from 'react'
import { createRootRoute, createRoute, createRouter, Outlet, redirect } from '@tanstack/react-router'
import { AppShell } from '@/components/app-shell'
import { useAuthStore } from '@/stores/auth-store'
import { Skeleton } from '@/components/ui'

function lazyPage(loader: () => Promise<Record<string, ComponentType>>) {
  const Component = lazy(async () => { const module = await loader(); const first = Object.values(module)[0] as ComponentType; return { default: first } })
  return () => <Suspense fallback={<div className='space-y-4'><Skeleton className='h-10 w-64'/><Skeleton className='h-72 w-full'/></div>}><Component /></Suspense>
}
const DashboardPage=lazyPage(()=>import('@/features/dashboard/dashboard-page'))
const SignInPage=lazyPage(()=>import('@/features/auth/sign-in-page'))
const RegisterPage=lazyPage(()=>import('@/features/auth/register-page'))
const KnowledgeBaseListPage=lazyPage(()=>import('@/features/knowledge-bases/knowledge-base-list-page'))
const KnowledgeBaseDetailPage=lazyPage(()=>import('@/features/knowledge-bases/knowledge-base-detail-page'))
const TenantsPage=lazyPage(()=>import('@/features/tenants/tenants-page'))
const ChatPage=lazyPage(()=>import('@/features/chat/chat-page'))
const IngestionPage=lazyPage(()=>import('@/features/operations/ingestion-page'))
const PipelinePage=lazyPage(()=>import('@/features/operations/pipeline-page'))
const SettingsPage=lazyPage(()=>import('@/features/settings/settings-page'))

const rootRoute=createRootRoute({component:()=> <Outlet/>})
const publicGuard=()=>{if(useAuthStore.getState().refreshToken)throw redirect({to:'/'})}; const authGuard=()=>{if(!useAuthStore.getState().refreshToken)throw redirect({to:'/sign-in'})}
const signInRoute=createRoute({getParentRoute:()=>rootRoute,path:'/sign-in',beforeLoad:publicGuard,component:SignInPage}); const registerRoute=createRoute({getParentRoute:()=>rootRoute,path:'/register',beforeLoad:publicGuard,component:RegisterPage}); const authenticatedRoute=createRoute({getParentRoute:()=>rootRoute,id:'_authenticated',beforeLoad:authGuard,component:AppShell})
const routes=[createRoute({getParentRoute:()=>authenticatedRoute,path:'/',component:DashboardPage}),createRoute({getParentRoute:()=>authenticatedRoute,path:'/knowledge-bases',component:KnowledgeBaseListPage}),createRoute({getParentRoute:()=>authenticatedRoute,path:'/knowledge-bases/$knowledgeBaseId',component:KnowledgeBaseDetailPage}),createRoute({getParentRoute:()=>authenticatedRoute,path:'/chat',component:ChatPage}),createRoute({getParentRoute:()=>authenticatedRoute,path:'/ingestion',component:IngestionPage}),createRoute({getParentRoute:()=>authenticatedRoute,path:'/pipeline',component:PipelinePage}),createRoute({getParentRoute:()=>authenticatedRoute,path:'/tenants',component:TenantsPage}),createRoute({getParentRoute:()=>authenticatedRoute,path:'/settings',component:SettingsPage})]
const routeTree=rootRoute.addChildren([signInRoute,registerRoute,authenticatedRoute.addChildren(routes)]); export const router=createRouter({routeTree,defaultPreload:'intent'}); declare module '@tanstack/react-router'{interface Register{router:typeof router}}
