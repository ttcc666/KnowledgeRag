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

const schema = z.object({ displayName: z.string().min(1, '请输入显示名'), email: z.string().email('请输入有效邮箱'), password: z.string().min(8, '密码至少 8 位') })
type FormValues = z.infer<typeof schema>

export function RegisterPage() {
  const navigate = useNavigate(); const setSession = useAuthStore((s) => s.setSession); const [submitting, setSubmitting] = useState(false)
  const { register, handleSubmit, formState: { errors } } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { displayName: '', email: '', password: '' } })
  const submit = handleSubmit(async (values) => { setSubmitting(true); try { const session = await api.auth.register(values); setSession(session); toast.success('注册成功，已创建个人租户'); navigate({ to: '/' }) } catch (error) { toast.error(getErrorMessage(error)) } finally { setSubmitting(false) } })
  return <div className='grid min-h-svh place-items-center bg-muted/30 p-4'><Card className='w-full max-w-md'><CardHeader className='space-y-3'><div className='flex size-11 items-center justify-center rounded-xl bg-primary text-primary-foreground'><BookOpen className='size-5' /></div><div><CardTitle className='text-2xl'>创建账号</CardTitle><CardDescription className='mt-2'>注册后会自动创建个人租户，之后可邀请成员加入。</CardDescription></div></CardHeader><CardContent><form onSubmit={submit} className='space-y-4'><div className='space-y-2'><Label>显示名</Label><Input {...register('displayName')} />{errors.displayName && <p className='text-xs text-destructive'>{errors.displayName.message}</p>}</div><div className='space-y-2'><Label>邮箱</Label><Input type='email' {...register('email')} />{errors.email && <p className='text-xs text-destructive'>{errors.email.message}</p>}</div><div className='space-y-2'><Label>密码</Label><Input type='password' {...register('password')} />{errors.password && <p className='text-xs text-destructive'>{errors.password.message}</p>}</div><Button className='w-full' disabled={submitting}>{submitting ? '注册中…' : '注册并登录'}</Button><div className='text-center text-sm text-muted-foreground'>已有账号？ <button type='button' className='font-medium text-foreground underline underline-offset-4' onClick={() => navigate({ to: '/sign-in' })}>返回登录</button></div></form></CardContent></Card></div>
}
