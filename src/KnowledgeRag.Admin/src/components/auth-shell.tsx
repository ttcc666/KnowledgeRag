import type { ReactNode } from 'react'
import { BookOpen, Bot, Database, Radio, Sparkles } from 'lucide-react'
import { motion } from 'motion/react'

const features = [
  { icon: Bot, title: 'Agent-native RAG', description: 'Microsoft Agent Framework 与多轮 Session。' },
  { icon: Database, title: 'Hybrid retrieval', description: 'Vector + BM25/FTS + RRF + reranker。' },
  { icon: Radio, title: 'Realtime ingestion', description: 'CAP 后台处理，SignalR 实时反馈。' },
]

export function AuthShell({ children, title, description }: { children: ReactNode; title: string; description: string }) {
  return <div className='grid min-h-svh bg-background lg:grid-cols-[minmax(0,1.15fr)_minmax(420px,.85fr)]'>
    <section className='relative hidden overflow-hidden border-r lg:flex lg:flex-col lg:justify-between lg:p-12 xl:p-16'>
      <div className='pointer-events-none absolute inset-0 app-grid opacity-50 [mask-image:linear-gradient(to_bottom,black,transparent_88%)]' /><div className='pointer-events-none absolute -left-24 top-12 size-[420px] rounded-full bg-primary/12 blur-3xl' /><div className='pointer-events-none absolute bottom-0 right-0 size-[360px] rounded-full bg-violet-500/10 blur-3xl' />
      <div className='relative flex items-center gap-3'><div className='flex size-10 items-center justify-center rounded-2xl bg-gradient-to-br from-primary to-violet-500 text-primary-foreground shadow-lg shadow-primary/20'><BookOpen className='size-5' /></div><div><div className='font-semibold tracking-tight'>Knowledge RAG</div><div className='text-[10px] uppercase tracking-[0.16em] text-muted-foreground'>AI Knowledge Platform</div></div></div>
      <div className='relative max-w-2xl'><motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: .45 }}><div className='mb-4 inline-flex items-center gap-2 rounded-full border bg-background/60 px-3 py-1.5 text-xs font-medium text-muted-foreground backdrop-blur'><Sparkles className='size-3.5 text-primary' />Knowledge operations, not another admin panel</div><h1 className='text-balance text-4xl font-semibold leading-tight tracking-[-0.05em] xl:text-5xl'>把文档变成可以被 AI<br />理解和使用的知识。</h1><p className='mt-5 max-w-xl text-base leading-7 text-muted-foreground'>统一管理知识摄取、检索、Agent 对话、权限与可观测性，保留每一个来源和处理状态。</p></motion.div><div className='mt-10 grid gap-3 xl:grid-cols-3'>{features.map(({ icon: Icon, title: featureTitle, description: featureDescription }, index) => <motion.div key={featureTitle} initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: .15 + index * .07 }} className='rounded-2xl border bg-card/55 p-4 backdrop-blur'><div className='flex size-8 items-center justify-center rounded-lg bg-primary/10 text-primary'><Icon className='size-4' /></div><div className='mt-3 text-sm font-semibold'>{featureTitle}</div><div className='mt-1 text-xs leading-5 text-muted-foreground'>{featureDescription}</div></motion.div>)}</div></div>
      <div className='relative text-xs text-muted-foreground'>Non-Azure · .NET 10 · React 19</div>
    </section>
    <section className='flex min-h-svh items-center justify-center p-5 sm:p-8 lg:p-12'><motion.div initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: .3 }} className='w-full max-w-md'><div className='mb-8 lg:hidden'><div className='flex items-center gap-3'><div className='flex size-10 items-center justify-center rounded-2xl bg-gradient-to-br from-primary to-violet-500 text-primary-foreground'><BookOpen className='size-5' /></div><div><div className='font-semibold'>Knowledge RAG</div><div className='text-[10px] uppercase tracking-[0.16em] text-muted-foreground'>AI Knowledge Platform</div></div></div></div><div className='mb-7'><h2 className='text-3xl font-semibold tracking-[-0.04em]'>{title}</h2><p className='mt-2 text-sm leading-6 text-muted-foreground'>{description}</p></div>{children}</motion.div></section>
  </div>
}
