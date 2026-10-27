import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { useNavigate } from '@tanstack/react-router'
import { BookOpen } from 'lucide-react'
import { z } from 'zod'
import { toast } from 'sonner'
import { api, getErrorMessage } from '@/lib/api'
import { useAuthStore } from '@/stores/auth-store'
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label } from '@/components/ui'

const schema = z.object({ email: z.string().email('请输入有效邮箱'), password: z.string().min(8, '密码至少 8 位') })
type FormValues = z.infer<typeof schema>

export function SignInPage() {
  const navigate = useNavigate()
  const setSession = useAuthStore((s) => s.setSession)
  const [submitting, setSubmitting] = useState(false)
  const { register, handleSubmit, formState: { errors } } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { email: 'demo@example.com', password: 'demo12345' } })
  const submit = handleSubmit(async (values) => {
    setSubmitting(true)
    try { const session = await api.auth.login(values); setSession(session); toast.success('登录成功'); navigate({ to: '/' }) }
    catch (error) { toast.error(getErrorMessage(error)) } finally { setSubmitting(false) }
  })
  return <div className='grid min-h-svh place-items-center bg-muted/30 p-4'><Card className='w-full max-w-md'><CardHeader className='space-y-3'><div className='flex size-11 items-center justify-center rounded-xl bg-primary text-primary-foreground'><BookOpen className='size-5' /></div><div><CardTitle className='text-2xl'>登录 Knowledge RAG</CardTitle><CardDescription className='mt-2'>使用你的账号进入多租户知识库管理后台。</CardDescription></div></CardHeader><CardContent><form onSubmit={submit} className='space-y-4'><div className='space-y-2'><Label htmlFor='email'>邮箱</Label><Input id='email' type='email' autoComplete='email' {...register('email')} />{errors.email && <p className='text-xs text-destructive'>{errors.email.message}</p>}</div><div className='space-y-2'><Label htmlFor='password'>密码</Label><Input id='password' type='password' autoComplete='current-password' {...register('password')} />{errors.password && <p className='text-xs text-destructive'>{errors.password.message}</p>}</div><Button className='w-full' disabled={submitting}>{submitting ? '登录中…' : '登录'}</Button><div className='text-center text-sm text-muted-foreground'>还没有账号？ <button type='button' className='font-medium text-foreground underline underline-offset-4' onClick={() => navigate({ to: '/register' })}>注册</button></div></form></CardContent></Card></div>
}
