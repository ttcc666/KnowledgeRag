import type { ChatMessage } from '@/lib/types'

export function isChatMessage(value: unknown): value is ChatMessage {
  if (!value || typeof value !== 'object') return false
  const message = value as Partial<ChatMessage>
  return typeof message.id === 'string'
    && message.id.length > 0
    && typeof message.role === 'string'
    && typeof message.content === 'string'
    && typeof message.createdAt === 'string'
}

export function flattenMessagePages(pages: readonly unknown[] | undefined): ChatMessage[] {
  if (!pages?.length) return []
  return [...pages].reverse().flatMap((page) => {
    if (!page || typeof page !== 'object') return []
    const pageMessages = (page as { messages?: unknown }).messages
    return Array.isArray(pageMessages) ? pageMessages.filter(isChatMessage) : []
  })
}

export function resolveRuntimeThreadId(
  conversationId: string,
  regular: readonly { id: string }[] | undefined,
  archived: readonly { id: string }[] | undefined,
): string | undefined {
  if (!conversationId) return undefined
  const exists = regular?.some((item) => item.id === conversationId)
    || archived?.some((item) => item.id === conversationId)
  return exists ? conversationId : undefined
}
