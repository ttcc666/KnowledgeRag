import type { RagProgressEvent } from '@/lib/types'

export function mergeProgressEvent(events: RagProgressEvent[], next: RagProgressEvent): RagProgressEvent[] {
  const index = events.findIndex((event) => event.stepId === next.stepId)
  if (index < 0) return [...events, next].slice(-80)
  const merged = events.slice()
  merged[index] = next
  return merged
}
