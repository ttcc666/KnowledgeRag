import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from './api'

const encoder = new TextEncoder()

describe('conversation SSE client', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('emits deltas and returns the completion payload', async () => {
    const body = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(encoder.encode('event: delta\ndata: {"text":"hello "}\n\n'))
        controller.enqueue(encoder.encode('event: delta\ndata: {"text":"world"}\n\n'))
        controller.enqueue(encoder.encode('event: complete\ndata: {"conversationId":"c1","answer":"hello world","sources":[]}\n\n'))
        controller.close()
      },
    })
    vi.stubGlobal('fetch', vi.fn(async () => new Response(body, { status: 200, headers: { 'Content-Type': 'text/event-stream' } })))
    const deltas: string[] = []
    const result = await api.conversations.stream('c1', 'question', (delta) => deltas.push(delta))
    expect(deltas).toEqual(['hello ', 'world'])
    expect(result.answer).toBe('hello world')
  })
})
