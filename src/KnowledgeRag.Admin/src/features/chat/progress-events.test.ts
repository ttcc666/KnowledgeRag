import { describe, expect, it } from 'vitest'
import type { RagProgressEvent } from '@/lib/types'
import { mergeProgressEvent } from './progress-events'

function event(stepId: string, sequence: number, status: string): RagProgressEvent {
  return {
    runId: 'run-1',
    sequence,
    stepId,
    stage: 'retrieval',
    type: `query.${status}`,
    actor: 'KnowledgeSearchService',
    label: '检索查询',
    status,
  }
}

describe('mergeProgressEvent', () => {
  it('updates a step instead of duplicating start and completion rows', () => {
    const started = event('query-1', 1, 'running')
    const completed = event('query-1', 4, 'completed')
    expect(mergeProgressEvent([started], completed)).toEqual([completed])
  })

  it('keeps parallel query steps as separate timeline rows', () => {
    const result = mergeProgressEvent([event('query-1', 1, 'running')], event('query-2', 2, 'running'))
    expect(result.map((item) => item.stepId)).toEqual(['query-1', 'query-2'])
  })

  it('bounds long repair timelines', () => {
    const events = Array.from({ length: 80 }, (_, index) => event(`step-${index}`, index + 1, 'completed'))
    expect(mergeProgressEvent(events, event('last', 81, 'running'))).toHaveLength(80)
  })
})
