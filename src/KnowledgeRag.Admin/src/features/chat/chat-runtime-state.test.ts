import { describe, expect, it } from 'vitest'
import { flattenMessagePages, resolveRuntimeThreadId } from './chat-runtime-state'

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
