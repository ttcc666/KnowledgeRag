import { expect, test, type Page } from '@playwright/test'

const session = {
  accessToken: 'test-access-token',
  refreshToken: 'test-refresh-token',
  expiresAt: '2099-01-01T00:00:00Z',
  refreshExpiresAt: '2099-02-01T00:00:00Z',
  user: { id: 'u1', email: 'product@example.com', displayName: 'Product Tester' },
}

async function authenticate(page: Page) {
  await page.addInitScript((value) => localStorage.setItem('knowledge-rag-admin-session', JSON.stringify(value)), session)
}

async function mockProductApi(page: Page) {
  let streamed = false
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url())
    const path = url.pathname
    if (path === '/api/dashboard') return route.fulfill({ json: { knowledgeBaseCount: 1, readyDocumentCount: 1, processingDocumentCount: 0, failedDocumentCount: 1, cancelledDocumentCount: 0, totalChunkCount: 24, conversationCount: 1, activity: [{ date: '2026-10-27', documents: 1, conversations: 1 }], recentIngestion: [{ documentId: 'd1', knowledgeBaseId: 'kb1', knowledgeBaseName: 'Product KB', fileName: 'handbook.pdf', status: 'Ready', chunkCount: 24, createdAt: '2026-10-27T00:00:00Z', updatedAt: '2026-10-27T00:02:00Z', elapsedSeconds: 120 }], retrieval: { queryCount: 8, p50LatencyMs: 95, p95LatencyMs: 210, noResultRate: 0.05, averageTopScore: 0.88, estimatedInputTokens: 1200, estimatedOutputTokens: 600, chatTurns: 4 } } })
    if (path === '/api/knowledge-bases') return route.fulfill({ json: [{ id: 'kb1', tenantId: 't1', name: 'Product KB', description: 'Enterprise knowledge', role: 'Owner', createdAt: '2026-10-27T00:00:00Z', updatedAt: '2026-10-27T00:00:00Z' }] })
    if (path === '/api/ingestion/jobs') return route.fulfill({ json: [{ documentId: 'd2', knowledgeBaseId: 'kb1', knowledgeBaseName: 'Product KB', fileName: 'failed.pdf', status: 'Failed', chunkCount: 0, lastError: 'embedding timeout', createdAt: '2026-10-27T00:00:00Z', updatedAt: '2026-10-27T00:01:00Z', elapsedSeconds: 60 }] })
    if (/\/api\/knowledge-bases\/kb1\/documents\/d2\/(retry|reindex|cancel)$/.test(path)) return route.fulfill({ status: 202, json: {} })
    if (path === '/api/conversations' && route.request().method() === 'GET') return route.fulfill({ json: [{ id: 'c1', knowledgeBaseId: 'kb1', title: 'Policy', createdAt: '2026-10-27T00:00:00Z', updatedAt: '2026-10-27T00:00:00Z' }] })
    if (path === '/api/conversations/c1/messages') return route.fulfill({ json: streamed ? [{ id: 'm1', role: 'user', content: 'What is the policy?', createdAt: '2026-10-27T00:00:00Z' }, { id: 'm2', role: 'assistant', content: 'The policy answer.', sourcesJson: JSON.stringify([{ documentId: 'd1', fileName: 'handbook.pdf', chunkIndex: 3, sourceLink: '/file', score: .94, excerpt: 'Employees receive the documented benefit.', pageNumber: 7, vectorScore: .91, bm25Score: 5.4, fusionScore: .031, rerankScore: .94 }]), createdAt: '2026-10-27T00:00:01Z' }] : [] })
    if (path === '/api/conversations/c1/messages/stream') {
      streamed = true
      return route.fulfill({ status: 200, contentType: 'text/event-stream', body: 'event: delta\ndata: {"text":"The policy "}\n\nevent: delta\ndata: {"text":"answer."}\n\nevent: complete\ndata: {"conversationId":"c1","answer":"The policy answer.","sources":[]}\n\n' })
    }
    return route.fulfill({ status: 200, json: [] })
  })
}

test.beforeEach(async ({ page }) => { await authenticate(page); await mockProductApi(page) })

test('dashboard uses aggregated operational metrics', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByText('Retrieval health')).toBeVisible()
  await expect(page.getByText('210 ms')).toBeVisible()
  await expect(page.getByText('handbook.pdf')).toBeVisible()
})

test('failed ingestion can be retried from operations', async ({ page }) => {
  await page.goto('/ingestion')
  await expect(page.getByText('embedding timeout')).toBeVisible()
  const request = page.waitForRequest((request) => request.url().endsWith('/api/knowledge-bases/kb1/documents/d2/retry'))
  await page.getByRole('button', { name: '重试' }).click()
  await request
})

test('chat streams and exposes auditable source evidence', async ({ page }) => {
  await page.goto('/chat')
  const composer = page.getByPlaceholder('询问当前知识库…')
  await composer.fill('What is the policy?')
  await composer.press('Enter')
  await expect(page.getByText('The policy answer.')).toBeVisible()
  await page.getByRole('button', { name: /handbook\.pdf/ }).click()
  await expect(page.getByText('Employees receive the documented benefit.').last()).toBeVisible()
  await expect(page.getByText('0.9400').last()).toBeVisible()
})
