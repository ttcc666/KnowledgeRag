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

function roleRank(role: string) {
  const normalized = role.toLowerCase()
  if (normalized === 'user') return 0
  if (normalized === 'assistant') return 1
  return 2
}

function compareMessages(a: ChatMessage, b: ChatMessage) {
  const aMs = Date.parse(a.createdAt)
  const bMs = Date.parse(b.createdAt)
  if (Number.isFinite(aMs) && Number.isFinite(bMs) && aMs !== bMs) return aMs - bMs

  // JavaScript Date truncates sub-millisecond precision. The API emits UTC ISO timestamps,
  // so the raw string preserves the extra fractional digits used to order one turn.
  const precise = a.createdAt.localeCompare(b.createdAt)
  if (precise !== 0) return precise

  const role = roleRank(a.role) - roleRank(b.role)
  if (role !== 0) return role
  return a.id.localeCompare(b.id)
}

export function flattenMessagePages(pages: readonly unknown[] | undefined): ChatMessage[] {
  if (!pages?.length) return []
  const byId = new Map<string, ChatMessage>()
  for (const page of [...pages].reverse()) {
    if (!page || typeof page !== 'object') continue
    const pageMessages = (page as { messages?: unknown }).messages
    if (!Array.isArray(pageMessages)) continue
    for (const message of pageMessages) {
      if (isChatMessage(message)) byId.set(message.id, message)
    }
  }
  return [...byId.values()].sort(compareMessages)
}

function matchesPersistedTurn(messages: readonly ChatMessage[], liveUser: string, liveAnswer: string) {
  if (!liveUser || !liveAnswer || messages.length < 2) return false
  const user = messages[messages.length - 2]
  const assistant = messages[messages.length - 1]
  return user.role.toLowerCase() === 'user'
    && assistant.role.toLowerCase() === 'assistant'
    && user.content === liveUser
    && assistant.content === liveAnswer
}

export function buildOptimisticThreadMessages(
  persisted: readonly ChatMessage[],
  liveUser: string,
  liveAnswer: string,
  streaming: boolean,
  liveSourcesJson?: string | null,
  createdAt = new Date().toISOString(),
): ChatMessage[] {
  const messages = [...persisted]

  // Once the server refetch contains the just-finished turn, keep the optimistic IDs until
  // streaming is cleared. This makes the hand-off atomic and prevents a duplicated/reordered
  // persisted user+assistant pair from appearing underneath the live turn.
  if (streaming && matchesPersistedTurn(messages, liveUser, liveAnswer)) messages.splice(-2)

  if (liveUser) messages.push({ id: 'live-user', role: 'user', content: liveUser, createdAt })
  if (streaming && liveUser) {
    messages.push({
      id: 'live-assistant',
      role: 'assistant',
      content: liveAnswer,
      sourcesJson: liveSourcesJson || undefined,
      createdAt,
    })
  } else if (liveAnswer) {
    messages.push({ id: 'live-assistant', role: 'assistant', content: liveAnswer, sourcesJson: liveSourcesJson || undefined, createdAt })
  }
  return messages
}

export function linkTrustedCitations(text: string, sourceCount: number, messageId: string) {
  if (!text || sourceCount <= 0) return text
  const lines = text.split('\n')
  let fenced = false
  return lines.map((line) => {
    const trimmed = line.trimStart()
    if (trimmed.startsWith('```') || trimmed.startsWith('~~~')) {
      fenced = !fenced
      return line
    }
    if (fenced) return line

    const segments = line.split(/(`+[^`]*`+)/g)
    return segments.map((segment, index) => {
      if (index % 2 === 1) return segment
      return segment.replace(/(?<!!)\[(\d+)\](?!\s*\()/g, (match, rawNumber: string) => {
        const citation = Number(rawNumber)
        if (!Number.isInteger(citation) || citation < 1 || citation > sourceCount) return match
        return `[${citation}](#kr-citation-${citation}-${messageId})`
      })
    }).join('')
  }).join('\n')
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
