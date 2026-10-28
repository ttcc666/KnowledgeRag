import { describe, expect, it } from 'vitest'
import { buildOptimisticThreadMessages, flattenMessagePages, linkTrustedCitations, resolveRuntimeThreadId } from './chat-runtime-state'

const user = { id: 'u1', role: 'user', content: 'hello', createdAt: '2026-10-28T00:00:00Z' }
const assistant = { id: 'a1', role: 'assistant', content: 'answer', createdAt: '2026-10-28T00:00:01Z' }
const older = { id: 'u0', role: 'user', content: 'older', createdAt: '2026-10-27T23:59:00Z' }

describe('flattenMessagePages', () => {
  it('keeps chronological page order while dropping undefined, null and malformed entries', () => {
    const result = flattenMessagePages([
      { messages: [undefined, user, null, { id: 'broken' }, assistant] },
      { messages: [older] },
    ])

    expect(result).toEqual([older, user, assistant])
  })

  it('puts the user before the assistant when browser-visible timestamps are tied', () => {
    const tiedUser = { ...user, createdAt: '2026-10-28T00:00:00.1230000+00:00' }
    const tiedAssistant = { ...assistant, createdAt: '2026-10-28T00:00:00.1230000+00:00' }
    expect(flattenMessagePages([{ messages: [tiedAssistant, tiedUser] }])).toEqual([tiedUser, tiedAssistant])
  })

  it('preserves sub-millisecond server precision before applying the role tie-breaker', () => {
    const preciseUser = { ...user, createdAt: '2026-10-28T00:00:00.1234567+00:00' }
    const preciseAssistant = { ...assistant, createdAt: '2026-10-28T00:00:00.1234568+00:00' }
    expect(flattenMessagePages([{ messages: [preciseAssistant, preciseUser] }])).toEqual([preciseUser, preciseAssistant])
  })

  it('deduplicates overlapping message pages by message id', () => {
    expect(flattenMessagePages([{ messages: [user, assistant] }, { messages: [older, user] }])).toEqual([older, user, assistant])
  })

  it('ignores malformed cached pages instead of crashing the chat page', () => {
    expect(flattenMessagePages([undefined, null, [], { messages: 'invalid' }])).toEqual([])
  })
})

describe('resolveRuntimeThreadId', () => {
  it('only exposes a controlled thread id after it exists in the regular or archived list', () => {
    expect(resolveRuntimeThreadId('c1', [{ id: 'c1' }], [])).toBe('c1')
    expect(resolveRuntimeThreadId('c2', [], [{ id: 'c2' }])).toBe('c2')
    expect(resolveRuntimeThreadId('pending', [{ id: 'c1' }], [{ id: 'c2' }])).toBeUndefined()
    expect(resolveRuntimeThreadId('', [{ id: 'c1' }], [])).toBeUndefined()
  })
})

describe('buildOptimisticThreadMessages', () => {
  it('creates a live assistant turn immediately so workflow progress belongs to the conversation', () => {
    const now = '2026-10-28T00:00:02Z'
    expect(buildOptimisticThreadMessages([], 'question', '', true, null, now)).toEqual([
      { id: 'live-user', role: 'user', content: 'question', createdAt: now },
      { id: 'live-assistant', role: 'assistant', content: '', sourcesJson: undefined, createdAt: now },
    ])
  })

  it('keeps the optimistic turn while the persisted hand-off is being refetched', () => {
    const now = '2026-10-28T00:00:02Z'
    const persisted = [
      { id: 'server-user', role: 'user', content: 'question', createdAt: now },
      { id: 'server-assistant', role: 'assistant', content: 'validated answer', createdAt: now },
    ]
    expect(buildOptimisticThreadMessages(persisted, 'question', 'validated answer', true, '[{"documentId":"d1"}]', now)).toEqual([
      { id: 'live-user', role: 'user', content: 'question', createdAt: now },
      { id: 'live-assistant', role: 'assistant', content: 'validated answer', sourcesJson: '[{"documentId":"d1"}]', createdAt: now },
    ])
  })
})

describe('linkTrustedCitations', () => {
  it('links only in-range citation markers for the current assistant message', () => {
    expect(linkTrustedCitations('Alpha [1], beta [2], invalid [3].', 2, 'm2')).toBe(
      'Alpha [1](#kr-citation-1-m2), beta [2](#kr-citation-2-m2), invalid [3].',
    )
  })

  it('does not rewrite code spans, fenced code or existing markdown links', () => {
    const input = '`[1]`\n\n```txt\n[1]\n```\n\n[1](https://example.test) [1]'
    expect(linkTrustedCitations(input, 1, 'm2')).toBe(
      '`[1]`\n\n```txt\n[1]\n```\n\n[1](https://example.test) [1](#kr-citation-1-m2)',
    )
  })
})
