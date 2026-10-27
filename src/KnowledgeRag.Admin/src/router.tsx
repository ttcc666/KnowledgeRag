import { createRootRoute, createRoute, createRouter, Outlet, redirect } from '@tanstack/react-router'
import { AppShell } from '@/components/app-shell'
import { DashboardPage } from '@/features/dashboard/dashboard-page'
import { SignInPage } from '@/features/auth/sign-in-page'
import { RegisterPage } from '@/features/auth/register-page'
import { KnowledgeBaseListPage } from '@/features/knowledge-bases/knowledge-base-list-page'
import { KnowledgeBaseDetailPage } from '@/features/knowledge-bases/knowledge-base-detail-page'
import { TenantsPage } from '@/features/tenants/tenants-page'
import { ChatPage } from '@/features/chat/chat-page'
import { useAuthStore } from '@/stores/auth-store'

const rootRoute = createRootRoute({ component: () => <Outlet /> })
const publicGuard = () => { if (useAuthStore.getState().refreshToken) throw redirect({ to: '/' }) }
const authGuard = () => { if (!useAuthStore.getState().refreshToken) throw redirect({ to: '/sign-in' }) }

const signInRoute = createRoute({ getParentRoute: () => rootRoute, path: '/sign-in', beforeLoad: publicGuard, component: SignInPage })
const registerRoute = createRoute({ getParentRoute: () => rootRoute, path: '/register', beforeLoad: publicGuard, component: RegisterPage })
const authenticatedRoute = createRoute({ getParentRoute: () => rootRoute, id: '_authenticated', beforeLoad: authGuard, component: AppShell })
const dashboardRoute = createRoute({ getParentRoute: () => authenticatedRoute, path: '/', component: DashboardPage })
const knowledgeBasesRoute = createRoute({ getParentRoute: () => authenticatedRoute, path: '/knowledge-bases', component: KnowledgeBaseListPage })
const knowledgeBaseDetailRoute = createRoute({ getParentRoute: () => authenticatedRoute, path: '/knowledge-bases/$knowledgeBaseId', component: KnowledgeBaseDetailPage })
const tenantsRoute = createRoute({ getParentRoute: () => authenticatedRoute, path: '/tenants', component: TenantsPage })
const chatRoute = createRoute({ getParentRoute: () => authenticatedRoute, path: '/chat', component: ChatPage })

const routeTree = rootRoute.addChildren([signInRoute, registerRoute, authenticatedRoute.addChildren([dashboardRoute, knowledgeBasesRoute, knowledgeBaseDetailRoute, tenantsRoute, chatRoute])])
export const router = createRouter({ routeTree, defaultPreload: 'intent' })
declare module '@tanstack/react-router' { interface Register { router: typeof router } }
