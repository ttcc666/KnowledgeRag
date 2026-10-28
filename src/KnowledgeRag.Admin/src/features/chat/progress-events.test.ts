import { describe, expect, it } from 'vitest'
import type { RagProgressEvent } from '@/lib/types'
import { mergeProgressEvent } from './progress-events'

function event(stepId: string, sequence: number, status: string, stage = 'retrieval'): RagProgressEvent {
  return {
    runId: 'run-1',
    sequence,
    stepId,
    stage,
    type: stage === 'workflow' ? `workflow.${status}` : `query.${status}`,
    actor: stage === 'workflow' ? 'KnowledgeRagWorkflow' : 'KnowledgeSearchService',
    label: stage === 'workflow' ? '回答已完成' : '检索查询',
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

  it('settles stale running rows when the workflow reaches a terminal state', () => {
    const runningAnswer = event('answer', 2, 'running', 'answer')
    const completedWorkflow = event('workflow', 3, 'completed', 'workflow')
    const result = mergeProgressEvent([runningAnswer], completedWorkflow)
    expect(result.map((item) => [item.stepId, item.status])).toEqual([
      ['answer', 'completed'],
      ['workflow', 'completed'],
    ])
    expect(result.at(-1)?.label).toBe('回答已完成')
  })

  it('marks stale running rows failed when the workflow fails', () => {
    const runningAnswer = event('answer', 2, 'running', 'answer')
    const failedWorkflow = event('workflow', 3, 'failed', 'workflow')
    const result = mergeProgressEvent([runningAnswer], failedWorkflow)
    expect(result[0].status).toBe('failed')
    expect(result.at(-1)?.status).toBe('failed')
  })

  it('bounds long repair timelines', () => {
    const events = Array.from({ length: 80 }, (_, index) => event(`step-${index}`, index + 1, 'completed'))
    expect(mergeProgressEvent(events, event('last', 81, 'running'))).toHaveLength(80)
  })
})
