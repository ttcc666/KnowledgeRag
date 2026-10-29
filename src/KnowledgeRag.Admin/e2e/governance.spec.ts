import { expect, test, type Page } from '@playwright/test'

const session = {
  accessToken: 'test-access-token',
  refreshToken: 'test-refresh-token',
  expiresAt: '2099-01-01T00:00:00Z',
  refreshExpiresAt: '2099-02-01T00:00:00Z',
  user: { id: 'u1', email: 'governance@example.com', displayName: 'Governance Tester' },
}

async function authenticate(page: Page) {
  await page.addInitScript((value) => localStorage.setItem('knowledge-rag-admin-session', JSON.stringify(value)), session)
}

async function mockGovernanceApi(page: Page) {
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url())
    const path = url.pathname
    const method = route.request().method()
    if (path === '/api/tenants') return route.fulfill({ json: [{ id: 't1', name: 'Acme', slug: 'acme', role: 'Owner', createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' }] })
    if (path === '/api/knowledge-bases') return route.fulfill({ json: [{ id: 'kb1', tenantId: 't1', name: 'Governance KB', description: 'Policies', role: 'Owner', createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' }] })
    if (path === '/api/tenants/t1/ai/deployments') return route.fulfill({ json: [
      { id: 'dep-planner', tenantId: 't1', connectionId: 'conn1', connectionName: 'OpenAI Prod', displayName: 'Planner Prod', remoteModelName: 'gpt-small', modelType: 'chat', isEnabled: true, healthStatus: 'healthy', fallbackDeploymentId: null, updatedAt: '2026-10-28T00:00:00Z' },
      { id: 'dep-answer', tenantId: 't1', connectionId: 'conn1', connectionName: 'OpenAI Prod', displayName: 'Answer Prod', remoteModelName: 'gpt-strong', modelType: 'chat', isEnabled: true, healthStatus: 'healthy', fallbackDeploymentId: 'dep-planner', updatedAt: '2026-10-28T00:00:00Z' },
      { id: 'dep-rerank', tenantId: 't1', connectionId: 'conn1', connectionName: 'OpenAI Prod', displayName: 'Rerank Prod', remoteModelName: 'rerank-v1', modelType: 'rerank', isEnabled: true, healthStatus: 'unknown', fallbackDeploymentId: null, updatedAt: '2026-10-28T00:00:00Z' },
    ] })
    if (path === '/api/governance/overview') return route.fulfill({ json: { runCount: 12, averageLatencyMs: 1840, estimatedCostUsd: .1245, auditCount: 42, promptProfileCount: 2, activeWebhookCount: 1, evaluationRunCount: 3 } })
    if (path === '/api/governance/evaluation/datasets') return route.fulfill({ json: [{ id: 'ds1', tenantId: 't1', knowledgeBaseId: 'kb1', name: 'Regression', description: 'Core regression', createdByUserId: 'u1', createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' }] })
    if (path === '/api/governance/evaluation/datasets/ds1/cases') return route.fulfill({ json: [{ id: 'case1', datasetId: 'ds1', category: 'fact', query: 'What is the policy?', expectedAnswerContains: 'policy', requiresKnowledge: true, createdAt: '2026-10-28T00:00:00Z' }] })
    if (path === '/api/governance/evaluation/runs') return route.fulfill({ json: [{ id: 'er1', datasetId: 'ds1', knowledgeBaseId: 'kb1', status: 'completed', caseCount: 10, passedCount: 9, passRate: .9, groundednessRate: .95, averageLatencyMs: 1700, averageSourceCount: 4.2, resultsJson: '[]', createdByUserId: 'u1', createdAt: '2026-10-28T00:00:00Z', completedAt: '2026-10-28T00:01:00Z' }] })
    if (path === '/api/governance/knowledge-bases/kb1/rag-profile' && method === 'GET') return route.fulfill({ json: { knowledgeBaseId: 'kb1', enablePlanner: true, plannerMaxQueries: 3, multiQueryTopK: 6, maxMergedEvidence: 10, enableReviewer: true, reviewerMaxRetries: 1, reviewerMinConfidence: .7, plannerModel: '', answerModel: '', reviewerModel: '', plannerDeploymentId: 'dep-planner', answerDeploymentId: 'dep-answer', reviewerDeploymentId: 'dep-planner', rerankerDeploymentId: 'dep-rerank', promptProfileId: null, agentMode: 'specialists', monthlyBudgetUsd: 25, dailyRunLimit: 500, updatedByUserId: 'u1', updatedAt: '2026-10-28T00:00:00Z' } })
    if (path === '/api/governance/knowledge-bases/kb1/rag-profile' && method === 'PUT') return route.fulfill({ json: { knowledgeBaseId: 'kb1', ...(await route.request().postDataJSON()), updatedByUserId: 'u1', updatedAt: '2026-10-28T00:00:00Z' } })
    if (path === '/api/governance/prompts') return route.fulfill({ json: [{ id: 'p1', tenantId: 't1', knowledgeBaseId: 'kb1', name: 'Production RAG', version: 2, plannerPrompt: '', answerPrompt: 'Lead with conclusion', reviewerPrompt: '', isActive: true, updatedByUserId: 'u1', createdAt: '2026-10-27T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' }] })
    if (path === '/api/governance/runs') return route.fulfill({ json: [{ id: 'r1', tenantId: 't1', knowledgeBaseId: 'kb1', conversationId: 'c1', userId: 'u1', query: 'What is the policy?', answer: 'Policy answer', status: 'completed', answerModel: 'gpt-strong', answerDeploymentId: 'dep-answer', agentMode: 'specialists', sourceCount: 4, estimatedInputTokens: 500, estimatedOutputTokens: 120, estimatedCostUsd: .0034, latencyMs: 1800, traceJson: JSON.stringify([{ stepId: 'planner', label: '问题分析完成', actor: 'QueryPlannerAgent', status: 'completed', elapsedMs: 120 }]), createdAt: '2026-10-28T00:00:00Z' }] })
    if (path === '/api/governance/model-prices') return route.fulfill({ json: [{ id: 'mp1', tenantId: 't1', deploymentId: 'dep-answer', model: 'gpt-strong', inputCostPerMillionTokens: 2.5, outputCostPerMillionTokens: 10, isActive: true, updatedAt: '2026-10-28T00:00:00Z' }] })
    if (path === '/api/governance/audit') return route.fulfill({ json: [{ id: 'a1', tenantId: 't1', userId: 'u1', action: 'update', resourceType: 'knowledge-base', resourceId: 'kb1', summary: 'PUT /api/governance/knowledge-bases/kb1/rag-profile', ipAddress: '127.0.0.1', statusCode: 200, createdAt: '2026-10-28T00:00:00Z' }] })
    if (path === '/api/governance/webhooks/providers') return route.fulfill({ json: [
      { id: 'custom', name: '自定义 HTTP Webhook', description: '通用 JSON + 可选 HMAC-SHA256 签名。', urlPlaceholder: 'https://example.com/hooks/rag', secretLabel: 'HMAC 签名密钥（可选）', secretHint: '为每次投递生成 X-KnowledgeRag-Signature' },
      { id: 'slack', name: 'Slack', description: '发送到 Slack Incoming Webhook。', urlPlaceholder: 'https://hooks.slack.com/services/...', secretLabel: '无需填写', secretHint: 'Slack 使用 Webhook URL 作为凭据' },
    ] })
    if (path === '/api/governance/webhooks') return route.fulfill({ json: [{ id: 'w1', tenantId: 't1', name: 'Ops', provider: 'custom', url: 'https://example.com/hook', eventTypes: 'rag.run.completed,evaluation.completed', isActive: true, lastStatusCode: 200, lastDeliveredAt: '2026-10-28T00:00:00Z', createdByUserId: 'u1', createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z', hasSecret: true }] })
    return route.fulfill({ status: 200, json: [] })
  })
}

test.beforeEach(async ({ page }) => { await authenticate(page); await mockGovernanceApi(page) })

test('governance center exposes evaluation, profile, trace, cost, audit, automation and agents', async ({ page }) => {
  await page.goto('/governance')
  await expect(page.getByRole('heading', { name: 'AI Governance Center' })).toBeVisible()
  await expect(page.getByText('12', { exact: true })).toBeVisible()
  await expect(page.getByRole('combobox').filter({ hasText: 'Regression' })).toBeVisible()
  await expect(page.getByText('90%', { exact: true })).toBeVisible()

  await page.getByRole('button', { name: 'RAG 配置' }).click()
  await expect(page.getByText('Retrieval & Review')).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Planner deployment' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Answer deployment' })).toBeVisible()
  const saveRequest = page.waitForRequest((request) => request.url().includes('/api/governance/knowledge-bases/kb1/rag-profile') && request.method() === 'PUT')
  await page.getByRole('button', { name: '保存 Profile' }).click()
  const saved = await saveRequest
  const body = saved.postDataJSON() as Record<string, unknown>
  expect(body.plannerDeploymentId).toBe('dep-planner')
  expect(body.answerDeploymentId).toBe('dep-answer')
  expect(body.reviewerDeploymentId).toBe('dep-planner')
  expect(body.rerankerDeploymentId).toBe('dep-rerank')

  await page.getByRole('button', { name: 'Run Trace' }).click()
  await page.getByText('What is the policy?').click()
  await expect(page.getByText('问题分析完成')).toBeVisible()

  await page.getByRole('button', { name: '成本 / 模型' }).click()
  await expect(page.getByText('Answer Prod')).toBeVisible()
  await expect(page.getByText('$0.1245').last()).toBeVisible()

  await page.getByRole('button', { name: 'Audit Log' }).click()
  await expect(page.getByRole('cell', { name: 'knowledge-base', exact: true })).toBeVisible()

  await page.getByRole('button', { name: 'Automation' }).click()
  await expect(page.getByText('https://example.com/hook')).toBeVisible()

  await page.getByRole('button', { name: 'Multi Agent' }).click()
  await expect(page.getByText('ComparisonSpecialistAgent')).toBeVisible()
  await expect(page.getByText('ResearchSpecialistAgent')).toBeVisible()
})
