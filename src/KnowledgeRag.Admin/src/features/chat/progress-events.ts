import type { RagProgressEvent } from '@/lib/types'

function settleTerminalRun(events: RagProgressEvent[], next: RagProgressEvent) {
  if (next.stage !== 'workflow' || (next.status !== 'completed' && next.status !== 'failed')) return events
  const terminalStatus = next.status === 'failed' ? 'failed' : 'completed'
  return events.map((event) => event.runId === next.runId && event.status === 'running'
    ? { ...event, status: terminalStatus }
    : event)
}

function orderBySequence(events: RagProgressEvent[]) {
  return events
    .sort((a, b) => a.sequence - b.sequence || a.stepId.localeCompare(b.stepId))
    .slice(-80)
}

export function mergeProgressEvent(events: RagProgressEvent[], next: RagProgressEvent): RagProgressEvent[] {
  const settled = settleTerminalRun(events, next)
  const index = settled.findIndex((event) => event.stepId === next.stepId)
  if (index < 0) return orderBySequence([...settled, next])
  const merged = settled.slice()
  merged[index] = next
  return orderBySequence(merged)
}
