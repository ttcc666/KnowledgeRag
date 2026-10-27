import * as React from 'react'
import { Slot } from '@radix-ui/react-slot'
import { cn } from '@/lib/utils'

export function Button({ className, variant = 'default', size = 'default', asChild = false, ...props }: React.ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'default' | 'secondary' | 'outline' | 'ghost' | 'destructive' | 'soft'; size?: 'default' | 'sm' | 'lg' | 'icon' | 'icon-sm'; asChild?: boolean }) {
  const Comp = asChild ? Slot : 'button'
  return <Comp className={cn('inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-lg text-sm font-medium transition-[background,color,border,box-shadow,transform] duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/55 disabled:pointer-events-none disabled:opacity-50 active:scale-[.985]', variant === 'default' && 'bg-primary text-primary-foreground shadow-sm hover:bg-primary/90 hover:shadow-md', variant === 'secondary' && 'bg-secondary text-secondary-foreground hover:bg-secondary/80', variant === 'soft' && 'border border-primary/10 bg-primary/8 text-primary hover:bg-primary/14', variant === 'outline' && 'border bg-background/65 shadow-sm hover:border-foreground/15 hover:bg-accent/70', variant === 'ghost' && 'hover:bg-accent/80 hover:text-accent-foreground', variant === 'destructive' && 'bg-destructive text-white shadow-sm hover:bg-destructive/90', size === 'default' && 'h-9 px-4 py-2', size === 'lg' && 'h-11 rounded-xl px-5', size === 'sm' && 'h-8 px-3 text-xs', size === 'icon' && 'size-9', size === 'icon-sm' && 'size-8', className)} {...props} />
}
