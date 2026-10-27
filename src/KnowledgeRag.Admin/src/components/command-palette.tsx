import { useEffect, useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { Building2, Database, LayoutDashboard, MessageSquareText, Search } from 'lucide-react'
import { api } from '@/lib/api'
import { Dialog, DialogContent, Input, Kbd } from '@/components/ui'

const destinations = [
  { label: '总览', hint: 'Overview', to: '/', icon: LayoutDashboard },
  { label: '知识库', hint: 'Knowledge bases', to: '/knowledge-bases', icon: Database },
  { label: 'RAG 对话', hint: 'Conversations', to: '/chat', icon: MessageSquareText },
  { label: '租户与权限', hint: 'Team & access', to: '/tenants', icon: Building2 },
]

export function CommandPalette({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const navigate = useNavigate(); const [query, setQuery] = useState('')
  const kbs = useQuery({ queryKey: ['knowledge-bases'], queryFn: api.knowledgeBases.list, enabled: open })
  useEffect(() => { if (!open) setQuery('') }, [open])
  const items = useMemo(() => {
    const q = query.trim().toLowerCase()
    return [...destinations.map((x) => ({ ...x, key: `nav-${x.to}` })), ...(kbs.data || []).map((kb) => ({ label: kb.name, hint: kb.description || 'Knowledge base', to: `/knowledge-bases/${kb.id}`, icon: Database, key: `kb-${kb.id}` }))].filter((x) => !q || `${x.label} ${x.hint}`.toLowerCase().includes(q))
  }, [kbs.data, query])
  const go = (to: string) => { onOpenChange(false); void navigate({ to }) }
  return <Dialog open={open} onOpenChange={onOpenChange}><DialogContent className='top-[18%] max-w-xl translate-y-0 gap-0 overflow-hidden p-0'><div className='flex items-center gap-3 border-b px-4'><Search className='size-4 text-muted-foreground' /><Input autoFocus value={query} onChange={(e) => setQuery(e.target.value)} placeholder='搜索页面、知识库或功能…' className='h-13 border-0 bg-transparent px-0 shadow-none focus-visible:ring-0' /><Kbd>ESC</Kbd></div><div className='max-h-[420px] overflow-y-auto p-2'>{items.length ? items.map(({ key, label, hint, to, icon: Icon }) => <button key={key} onClick={() => go(to)} className='flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-left transition hover:bg-accent'><div className='flex size-8 items-center justify-center rounded-lg border bg-background'><Icon className='size-4 text-muted-foreground' /></div><div className='min-w-0 flex-1'><div className='truncate text-sm font-medium'>{label}</div><div className='truncate text-xs text-muted-foreground'>{hint}</div></div></button>) : <div className='px-4 py-12 text-center text-sm text-muted-foreground'>没有匹配结果</div>}</div></DialogContent></Dialog>
}
