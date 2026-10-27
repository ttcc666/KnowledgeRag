import { create } from 'zustand'
import type { AuthResponse, UserSummary } from '@/lib/types'

const STORAGE_KEY = 'knowledge-rag-admin-session'
interface PersistedSession { accessToken: string; refreshToken: string; expiresAt: string; refreshExpiresAt: string; user: UserSummary }
interface AuthState extends PersistedSession {
  setSession: (session: AuthResponse) => void
  reset: () => void
}
const empty: PersistedSession = { accessToken: '', refreshToken: '', expiresAt: '', refreshExpiresAt: '', user: { id: '', email: '', displayName: '' } }
function load(): PersistedSession {
  try { return { ...empty, ...JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}') } } catch { return empty }
}
export const useAuthStore = create<AuthState>((set) => ({
  ...load(),
  setSession: (session) => {
    const next: PersistedSession = { accessToken: session.accessToken, refreshToken: session.refreshToken, expiresAt: session.expiresAt, refreshExpiresAt: session.refreshExpiresAt, user: session.user }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(next)); set(next)
  },
  reset: () => { localStorage.removeItem(STORAGE_KEY); set(empty) },
}))
