import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { useNavigate } from '@tanstack/react-router'
import { ArrowRight, LockKeyhole, Mail } from 'lucide-react'
import { z } from 'zod'
import { toast } from 'sonner'
import { api, getErrorMessage } from '@/lib/api'
import { useAuthStore } from '@/stores/auth-store'
import { AuthShell } from '@/components/auth-shell'
import { Button, Input, Label } from '@/components/ui'

const schema = z.object({ email: z.string().email('请输入有效邮箱'), password: z.string().min(8, '密码至少 8 位') })
type FormValues = z.infer<typeof schema>

export function SignInPage() {
  const navigate = useNavigate(); const setSession = useAuthStore((s) => s.setSession); const [submitting, setSubmitting] = useState(false)
  const { register, handleSubmit, formState: { errors } } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { email: 'demo@example.com', password: 'demo12345' } })
  const submit = handleSubmit(async (values) => { setSubmitting(true); try { const session = await api.auth.login(values); setSession(session); toast.success('欢迎回来'); navigate({ to: '/' }) } catch (error) { toast.error(getErrorMessage(error)) } finally { setSubmitting(false) } })
  return <AuthShell title='欢迎回来' description='登录你的工作空间，继续管理知识、检索和 Agent 对话。'><form onSubmit={submit} className='space-y-5'><div className='space-y-2'><Label htmlFor='email'>邮箱</Label><div className='relative'><Mail className='absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground' /><Input id='email' type='email' autoComplete='email' className='pl-9' {...register('email')} /></div>{errors.email && <p className='text-xs text-destructive'>{errors.email.message}</p>}</div><div className='space-y-2'><Label htmlFor='password'>密码</Label><div className='relative'><LockKeyhole className='absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground' /><Input id='password' type='password' autoComplete='current-password' className='pl-9' {...register('password')} /></div>{errors.password && <p className='text-xs text-destructive'>{errors.password.message}</p>}</div><Button size='lg' className='w-full' disabled={submitting}>{submitting ? '正在登录…' : <>进入工作空间<ArrowRight className='size-4' /></>}</Button><div className='text-center text-sm text-muted-foreground'>还没有账号？ <button type='button' className='font-medium text-primary hover:underline' onClick={() => navigate({ to: '/register' })}>创建账号</button></div></form></AuthShell>
}
