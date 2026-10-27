import { useEffect, useState } from 'react'
import { Link, Outlet, useNavigate } from '@tanstack/react-router'
import { BookOpen, Building2, Database, KeyRound, LayoutDashboard, LogOut, Menu, MessageSquareText, Moon, Search, Sun, X } from 'lucide-react'
import { useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api'
import { cn } from '@/lib/utils'
import { useAuthStore } from '@/stores/auth-store'
import { Button } from './ui'

const nav = [
  { to: '/', label: '总览', icon: LayoutDashboard },
  { to: '/knowledge-bases', label: '知识库', icon: Database },
  { to: '/chat', label: 'RAG 对话', icon: MessageSquareText },
  { to: '/tenants', label: '租户与权限', icon: Building2 },
] as const

function NavLink({ item, onClick }: { item: (typeof nav)[number]; onClick?: () => void }) {
  const Icon = item.icon
  return <Link to={item.to} onClick={onClick} activeOptions={{ exact: item.to === '/' }} className='flex items-center gap-3 rounded-md px-3 py-2 text-sm text-muted-foreground transition hover:bg-accent hover:text-accent-foreground' activeProps={{ className: 'bg-accent text-accent-foreground font-medium' }}><Icon className='size-4' />{item.label}</Link>
}

export function AppShell() {
  const [mobileOpen, setMobileOpen] = useState(false)
  const [dark, setDark] = useState(() => localStorage.getItem('theme') === 'dark')
  const auth = useAuthStore()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  useEffect(() => { document.documentElement.classList.toggle('dark', dark); localStorage.setItem('theme', dark ? 'dark' : 'light') }, [dark])
  const logout = async () => { try { await api.auth.logout(auth.refreshToken) } catch { /* ignore */ } auth.reset(); queryClient.clear(); navigate({ to: '/sign-in' }) }

  const sidebar = <div className='flex h-full flex-col bg-sidebar text-sidebar-foreground'>
    <div className='flex h-16 items-center gap-3 border-b border-sidebar-border px-5'><div className='flex size-9 items-center justify-center rounded-lg bg-primary text-primary-foreground'><BookOpen className='size-5' /></div><div><div className='font-semibold'>Knowledge RAG</div><div className='text-xs text-muted-foreground'>Agent Admin</div></div></div>
    <nav className='flex-1 space-y-1 p-3'>{nav.map((item) => <NavLink key={item.to} item={item} onClick={() => setMobileOpen(false)} />)}<div className='my-3 border-t' /><a href='/swagger' target='_blank' className='flex items-center gap-3 rounded-md px-3 py-2 text-sm text-muted-foreground hover:bg-accent'><Search className='size-4' />Swagger API</a></nav>
    <div className='border-t p-3'><div className='mb-2 px-3 py-2'><div className='truncate text-sm font-medium'>{auth.user.displayName || '用户'}</div><div className='truncate text-xs text-muted-foreground'>{auth.user.email}</div></div><button onClick={logout} className='flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm text-muted-foreground hover:bg-accent hover:text-accent-foreground'><LogOut className='size-4' />退出登录</button></div>
  </div>

  return <div className='min-h-svh bg-background'>
    <aside className='fixed inset-y-0 left-0 z-40 hidden w-64 border-r md:block'>{sidebar}</aside>
    {mobileOpen && <div className='fixed inset-0 z-50 md:hidden'><button aria-label='关闭菜单' className='absolute inset-0 bg-black/50' onClick={() => setMobileOpen(false)} /><aside className='relative h-full w-72 border-r bg-background shadow-xl'>{sidebar}<button className='absolute right-3 top-3 rounded-md p-2 hover:bg-accent' onClick={() => setMobileOpen(false)}><X className='size-4' /></button></aside></div>}
    <div className='md:pl-64'>
      <header className='sticky top-0 z-30 flex h-16 items-center justify-between border-b bg-background/95 px-4 backdrop-blur sm:px-6'><div className='flex items-center gap-2'><Button variant='ghost' size='icon' className='md:hidden' onClick={() => setMobileOpen(true)}><Menu className='size-5' /></Button><div className='hidden text-sm text-muted-foreground sm:block'>Microsoft Agent Framework · Multi-tenant RAG</div></div><div className='flex items-center gap-2'><Button variant='ghost' size='icon' onClick={() => setDark(!dark)} aria-label='切换主题'>{dark ? <Sun className='size-4' /> : <Moon className='size-4' />}</Button><div className='hidden rounded-full bg-muted px-3 py-1.5 text-xs sm:block'>{auth.user.displayName}</div></div></header>
      <main className='mx-auto w-full max-w-[1500px] p-4 sm:p-6 lg:p-8'><Outlet /></main>
    </div>
  </div>
}
