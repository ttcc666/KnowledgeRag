import type { ReactNode } from 'react'
import { motion } from 'motion/react'
import { Activity, ArrowUpRight } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Badge, Card, Progress } from '@/components/ui'
import type { DocumentStatus } from '@/lib/types'

export function PageTransition({ children, className }: { children: ReactNode; className?: string }) {
  return <motion.div initial={{ opacity: 0, y: 7 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: .24, ease: [0.22, 1, 0.36, 1] }} className={className}>{children}</motion.div>
}

export function LiveDot({ className }: { className?: string }) { return <span className={cn('relative flex size-2', className)}><span className='absolute inline-flex size-full animate-ping rounded-full bg-success opacity-50' /><span className='relative inline-flex size-2 rounded-full bg-success' /></span> }

export function StatusBadge({ status }: { status: DocumentStatus }) {
  const map: Record<DocumentStatus, { label: string; variant: 'secondary' | 'info' | 'success' | 'destructive' | 'outline' }> = {
    Queued: { label: 'Queued', variant: 'secondary' },
    Processing: { label: 'Processing', variant: 'info' },
    Ready: { label: 'Ready', variant: 'success' },
    Failed: { label: 'Failed', variant: 'destructive' },
    Cancelled: { label: 'Cancelled', variant: 'outline' },
  }
  return <Badge variant={map[status].variant}>{status === 'Processing' && <span className='mr-1.5'><LiveDot /></span>}{map[status].label}</Badge>
}

export function ProcessingBar({ progress, stage, chunkCount }: { progress: number; stage?: string; chunkCount?: number }) {
  return <div className='space-y-1.5'><div className='flex items-center justify-between text-[11px]'><span className='capitalize text-muted-foreground'>{stage || 'processing'}{chunkCount ? ` · ${chunkCount} chunks` : ''}</span><span className='font-medium tabular-nums'>{Math.round(progress)}%</span></div><Progress value={progress} /></div>
}

export function MetricTile({ label, value, hint, icon, accent = 'primary' }: { label: string; value: ReactNode; hint?: ReactNode; icon: ReactNode; accent?: 'primary' | 'success' | 'warning' | 'info' }) {
  const tones = { primary: 'bg-primary/10 text-primary', success: 'bg-success/10 text-success', warning: 'bg-warning/10 text-warning', info: 'bg-info/10 text-info' }
  return <Card className='group relative overflow-hidden border-border/80 bg-card/75 p-5 transition duration-200 hover:-translate-y-0.5 hover:border-foreground/10 hover:shadow-lg'><div className='pointer-events-none absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-primary/35 to-transparent opacity-0 transition group-hover:opacity-100' /><div className='flex items-start justify-between gap-3'><div><div className='text-xs font-medium text-muted-foreground'>{label}</div><div className='mt-2 text-3xl font-semibold tracking-[-0.04em]'>{value}</div>{hint && <div className='mt-1.5 text-xs text-muted-foreground'>{hint}</div>}</div><div className={cn('flex size-9 items-center justify-center rounded-xl', tones[accent])}>{icon}</div></div></Card>
}

export function SectionHeading({ title, description, action }: { title: string; description?: string; action?: ReactNode }) { return <div className='flex items-end justify-between gap-4'><div><h2 className='text-base font-semibold tracking-tight'>{title}</h2>{description && <p className='mt-1 text-sm text-muted-foreground'>{description}</p>}</div>{action}</div> }

export function SystemBadge({ label, value }: { label: string; value: string }) { return <div className='flex items-center justify-between gap-3 rounded-xl border bg-background/55 px-3 py-2.5'><div className='flex min-w-0 items-center gap-2'><Activity className='size-3.5 shrink-0 text-primary' /><span className='truncate text-xs font-medium'>{label}</span></div><span className='text-[11px] text-muted-foreground'>{value}</span></div> }

export function InlineLink({ children }: { children: ReactNode }) { return <span className='inline-flex items-center gap-1 font-medium text-primary'>{children}<ArrowUpRight className='size-3.5' /></span> }
