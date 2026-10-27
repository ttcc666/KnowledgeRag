import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { useNavigate } from '@tanstack/react-router'
import { ArrowRight, LockKeyhole, Mail, UserRound } from 'lucide-react'
import { z } from 'zod'
import { toast } from 'sonner'
import { api, getErrorMessage } from '@/lib/api'
import { useAuthStore } from '@/stores/auth-store'
import { AuthShell } from '@/components/auth-shell'
import { Button, Input, Label } from '@/components/ui'

const schema = z.object({ displayName: z.string().min(1, '请输入显示名'), email: z.string().email('请输入有效邮箱'), password: z.string().min(8, '密码至少 8 位') })
type FormValues = z.infer<typeof schema>

export function RegisterPage() {
  const navigate = useNavigate(); const setSession = useAuthStore((s) => s.setSession); const [submitting, setSubmitting] = useState(false)
  const { register, handleSubmit, formState: { errors } } = useForm<FormValues>({ resolver: zodResolver(schema) })
  const submit = handleSubmit(async (values) => { setSubmitting(true); try { const session = await api.auth.register(values); setSession(session); toast.success('工作空间已创建'); navigate({ to: '/' }) } catch (error) { toast.error(getErrorMessage(error)) } finally { setSubmitting(false) } })
  const field = (icon: typeof UserRound, id: keyof FormValues, type: string, label: string, autoComplete: string) => { const Icon = icon; return <div className='space-y-2'><Label htmlFor={id}>{label}</Label><div className='relative'><Icon className='absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground' /><Input id={id} type={type} autoComplete={autoComplete} className='pl-9' {...register(id)} /></div>{errors[id] && <p className='text-xs text-destructive'>{errors[id]?.message}</p>}</div> }
  return <AuthShell title='创建你的工作空间' description='注册后会自动创建个人租户，你可以继续添加知识库并邀请成员。'><form onSubmit={submit} className='space-y-4'>{field(UserRound, 'displayName', 'text', '显示名', 'name')}{field(Mail, 'email', 'email', '邮箱', 'email')}{field(LockKeyhole, 'password', 'password', '密码', 'new-password')}<Button size='lg' className='mt-2 w-full' disabled={submitting}>{submitting ? '正在创建…' : <>创建并进入<ArrowRight className='size-4' /></>}</Button><div className='text-center text-sm text-muted-foreground'>已有账号？ <button type='button' className='font-medium text-primary hover:underline' onClick={() => navigate({ to: '/sign-in' })}>返回登录</button></div></form></AuthShell>
}
