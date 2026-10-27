import { useEffect, useMemo, useState } from 'react'
import { Link, Outlet, useNavigate, useRouterState } from '@tanstack/react-router'
import {
  Activity,
  BookOpen,
  Building2,
  ChevronLeft,
  ChevronRight,
  Database,
  ExternalLink,
  LayoutDashboard,
  LogOut,
  Menu,
  MessageSquareText,
  Moon,
  Search,
  Settings2,
  Sun,
  Workflow,
  X,
  Zap,
} from 'lucide-react'
import { useQueryClient } from '@tanstack/react-query'
import { AnimatePresence, motion } from 'motion/react'
import { api } from '@/lib/api'
import { cn } from '@/lib/utils'
import { useAuthStore } from '@/stores/auth-store'
import { Button, Kbd } from './ui'
import { CommandPalette } from './command-palette'

const sections = [
  {
    label: 'Workspace',
    items: [
      { to: '/', label: '总览', icon: LayoutDashboard },
      { to: '/knowledge-bases', label: '知识库', icon: Database },
      { to: '/chat', label: 'RAG 对话', icon: MessageSquareText },
    ],
  },
  {
    label: 'Operate',
    items: [
      { to: '/ingestion', label: '摄取任务', icon: Activity },
      { to: '/pipeline', label: 'RAG Pipeline', icon: Workflow },
    ],
  },
  {
    label: 'Manage',
    items: [
      { to: '/tenants', label: '租户与权限', icon: Building2 },
      { to: '/settings', label: '设置', icon: Settings2 },
    ],
  },
] as const

type NavItem = (typeof sections)[number]['items'][number]

function NavLink({ item, collapsed, onClick }: { item: NavItem; collapsed: boolean; onClick?: () => void }) {
  const Icon = item.icon
  return (
    <Link
      to={item.to}
      onClick={onClick}
      activeOptions={{ exact: item.to === '/' }}
      title={collapsed ? item.label : undefined}
      className={cn(
        'group flex h-9 items-center gap-3 rounded-xl px-3 text-sm text-muted-foreground transition hover:bg-sidebar-accent hover:text-sidebar-accent-foreground',
        collapsed && 'justify-center px-0',
      )}
      activeProps={{ className: 'bg-sidebar-accent text-sidebar-accent-foreground font-medium' }}
    >
      <Icon className='size-4 shrink-0' />
      {!collapsed && <span className='truncate'>{item.label}</span>}
    </Link>
  )
}

export function AppShell() {
  const [mobileOpen, setMobileOpen] = useState(false)
  const [commandOpen, setCommandOpen] = useState(false)
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem('sidebar-collapsed') === 'true')
  const [dark, setDark] = useState(() => localStorage.getItem('theme') === 'dark')

  const auth = useAuthStore()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const isChat = pathname === '/chat'

  useEffect(() => {
    document.documentElement.classList.toggle('dark', dark)
    localStorage.setItem('theme', dark ? 'dark' : 'light')
  }, [dark])

  useEffect(() => {
    localStorage.setItem('sidebar-collapsed', String(collapsed))
  }, [collapsed])

  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setCommandOpen((current) => !current)
      }
    }
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [])

  const pageLabel = useMemo(() => {
    if (pathname.startsWith('/knowledge-bases/')) return 'Knowledge Base'
    if (pathname === '/knowledge-bases') return 'Knowledge Bases'
    if (pathname === '/chat') return 'Conversations'
    if (pathname === '/ingestion') return 'Ingestion Operations'
    if (pathname === '/pipeline') return 'RAG Pipeline'
    if (pathname === '/tenants') return 'Team & Access'
    if (pathname === '/settings') return 'Settings'
    return 'Overview'
  }, [pathname])

  const logout = async () => {
    try {
      await api.auth.logout(auth.refreshToken)
    } catch {
      // Always clear the local session even when the server-side session is gone.
    }
    auth.reset()
    queryClient.clear()
    await navigate({ to: '/sign-in' })
  }

  const renderSidebar = (mobile = false) => {
    const compact = collapsed && !mobile
    return (
      <div className='flex h-full flex-col bg-sidebar/95 text-sidebar-foreground backdrop-blur-xl'>
        <div className={cn('flex h-16 items-center border-b border-sidebar-border', compact ? 'justify-center px-2' : 'gap-3 px-4')}>
          <div className='relative flex size-9 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-primary to-violet-500 text-primary-foreground shadow-lg shadow-primary/15'>
            <BookOpen className='size-4.5' />
            <span className='absolute -right-0.5 -top-0.5 size-2.5 rounded-full border-2 border-sidebar bg-success' />
          </div>
          {!compact && (
            <div className='min-w-0'>
              <div className='truncate text-sm font-semibold'>Knowledge RAG</div>
              <div className='truncate text-[10px] uppercase tracking-[0.16em] text-muted-foreground'>AI Knowledge Platform</div>
            </div>
          )}
        </div>

        <nav className='flex-1 space-y-5 overflow-y-auto p-3'>
          {sections.map((section) => (
            <div key={section.label}>
              {!compact && <div className='mb-1.5 px-3 text-[10px] font-semibold uppercase tracking-[0.15em] text-muted-foreground/70'>{section.label}</div>}
              <div className='space-y-1'>
                {section.items.map((item) => (
                  <NavLink key={item.to} item={item} collapsed={compact} onClick={() => setMobileOpen(false)} />
                ))}
              </div>
            </div>
          ))}
          <div className='border-t border-sidebar-border pt-3'>
            <a
              href='/swagger'
              target='_blank'
              rel='noreferrer'
              className={cn(
                'flex h-9 items-center gap-3 rounded-xl px-3 text-sm text-muted-foreground transition hover:bg-sidebar-accent hover:text-foreground',
                compact && 'justify-center px-0',
              )}
              title={compact ? 'Swagger API' : undefined}
            >
              <ExternalLink className='size-4' />
              {!compact && 'Swagger API'}
            </a>
          </div>
        </nav>

        <div className='border-t border-sidebar-border p-3'>
          {!compact ? (
            <div className='rounded-xl border border-sidebar-border bg-background/35 p-2'>
              <div className='flex items-center gap-2.5'>
                <div className='flex size-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-xs font-semibold text-primary'>
                  {(auth.user.displayName || auth.user.email || 'U').slice(0, 1).toUpperCase()}
                </div>
                <div className='min-w-0 flex-1'>
                  <div className='truncate text-xs font-medium'>{auth.user.displayName || '用户'}</div>
                  <div className='truncate text-[10px] text-muted-foreground'>{auth.user.email}</div>
                </div>
                <button onClick={() => void logout()} className='rounded-lg p-1.5 text-muted-foreground hover:bg-accent hover:text-foreground' title='退出登录'>
                  <LogOut className='size-3.5' />
                </button>
              </div>
            </div>
          ) : (
            <button onClick={() => void logout()} className='flex size-9 w-full items-center justify-center rounded-xl text-muted-foreground hover:bg-sidebar-accent hover:text-foreground' title='退出登录'>
              <LogOut className='size-4' />
            </button>
          )}
        </div>
      </div>
    )
  }

  return (
    <div className='min-h-svh'>
      <aside className={cn('fixed inset-y-0 left-0 z-40 hidden border-r border-sidebar-border transition-[width] duration-200 md:block', collapsed ? 'w-[76px]' : 'w-[248px]')}>
        {renderSidebar()}
        <button onClick={() => setCollapsed((current) => !current)} className='absolute -right-3 top-20 flex size-6 items-center justify-center rounded-full border bg-background shadow-sm' aria-label={collapsed ? '展开侧边栏' : '折叠侧边栏'}>
          {collapsed ? <ChevronRight className='size-3.5' /> : <ChevronLeft className='size-3.5' />}
        </button>
      </aside>

      <AnimatePresence>
        {mobileOpen && (
          <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} className='fixed inset-0 z-50 md:hidden'>
            <button className='absolute inset-0 bg-slate-950/45 backdrop-blur-sm' onClick={() => setMobileOpen(false)} aria-label='关闭菜单' />
            <motion.aside initial={{ x: -300 }} animate={{ x: 0 }} exit={{ x: -300 }} transition={{ type: 'spring', stiffness: 360, damping: 34 }} className='relative h-full w-72 border-r bg-sidebar shadow-2xl'>
              {renderSidebar(true)}
              <button className='absolute right-3 top-3 rounded-lg p-2 hover:bg-accent' onClick={() => setMobileOpen(false)} aria-label='关闭菜单'>
                <X className='size-4' />
              </button>
            </motion.aside>
          </motion.div>
        )}
      </AnimatePresence>

      <div className={cn('transition-[padding] duration-200', collapsed ? 'md:pl-[76px]' : 'md:pl-[248px]')}>
        <header className='sticky top-0 z-30 flex h-16 items-center justify-between border-b bg-background/75 px-4 backdrop-blur-xl sm:px-6'>
          <div className='flex min-w-0 items-center gap-3'>
            <Button variant='ghost' size='icon' className='md:hidden' onClick={() => setMobileOpen(true)} aria-label='打开菜单'>
              <Menu className='size-5' />
            </Button>
            <div className='hidden items-center gap-2 sm:flex'>
              <span className='flex items-center gap-1.5 text-xs text-muted-foreground'><Zap className='size-3.5 text-primary' />Workspace</span>
              <span className='text-muted-foreground/40'>/</span>
              <span className='truncate text-xs font-medium'>{pageLabel}</span>
            </div>
          </div>
          <div className='flex items-center gap-2'>
            <button onClick={() => setCommandOpen(true)} className='hidden h-9 w-56 items-center gap-2 rounded-xl border bg-background/60 px-3 text-left text-xs text-muted-foreground lg:flex'>
              <Search className='size-3.5' /><span className='flex-1'>快速搜索</span><Kbd>⌘K</Kbd>
            </button>
            <Button variant='ghost' size='icon' onClick={() => setDark((current) => !current)} aria-label='切换主题'>
              {dark ? <Sun className='size-4' /> : <Moon className='size-4' />}
            </Button>
            <div className='hidden items-center gap-2 rounded-xl border bg-background/55 px-2.5 py-1.5 sm:flex'>
              <span className='size-1.5 rounded-full bg-success' />
              <span className='max-w-28 truncate text-xs font-medium'>{auth.user.displayName || 'User'}</span>
            </div>
          </div>
        </header>

        <main className={cn(isChat ? 'h-[calc(100svh-4rem)] overflow-hidden p-3 sm:p-4' : 'mx-auto w-full max-w-[1580px] p-4 sm:p-6 lg:p-8')}>
          <AnimatePresence mode='wait' initial={false}>
            <motion.div key={pathname} className={isChat ? 'chat-route h-full min-h-0' : undefined} initial={{ opacity: 0, y: 5 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -3 }} transition={{ duration: 0.18 }}>
              <Outlet />
            </motion.div>
          </AnimatePresence>
        </main>
      </div>

      <CommandPalette open={commandOpen} onOpenChange={setCommandOpen} />
    </div>
  )
}
