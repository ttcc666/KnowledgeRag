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
  let archived = false
  let title = 'Policy'
  const source = { documentId: 'd1', fileName: 'handbook.pdf', chunkIndex: 3, sourceLink: '/file', score: .94, excerpt: `Employees receive the documented benefit.\n${'Additional source evidence for scrolling verification. '.repeat(90)}`, pageNumber: 7, vectorScore: .91, bm25Score: 5.4, fusionScore: .031, rerankScore: .94 }

  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url())
    const path = url.pathname
    const method = route.request().method()
    if (path === '/api/dashboard') return route.fulfill({ json: { knowledgeBaseCount: 1, readyDocumentCount: 1, processingDocumentCount: 0, failedDocumentCount: 1, cancelledDocumentCount: 0, totalChunkCount: 24, conversationCount: 1, activity: [{ date: '2026-10-27', documents: 1, conversations: 1 }], recentIngestion: [{ documentId: 'd1', knowledgeBaseId: 'kb1', knowledgeBaseName: 'Product KB', fileName: 'handbook.pdf', status: 'Ready', chunkCount: 24, createdAt: '2026-10-27T00:00:00Z', updatedAt: '2026-10-27T00:02:00Z', elapsedSeconds: 120 }], retrieval: { queryCount: 8, p50LatencyMs: 95, p95LatencyMs: 210, noResultRate: 0.05, averageTopScore: 0.88, estimatedInputTokens: 1200, estimatedOutputTokens: 600, chatTurns: 4 } } })
    if (path === '/api/knowledge-bases') return route.fulfill({ json: [{ id: 'kb1', tenantId: 't1', name: 'Product KB', description: 'Enterprise knowledge', role: 'Owner', createdAt: '2026-10-27T00:00:00Z', updatedAt: '2026-10-27T00:00:00Z' }] })
    if (path === '/api/ingestion/jobs') return route.fulfill({ json: [{ documentId: 'd2', knowledgeBaseId: 'kb1', knowledgeBaseName: 'Product KB', fileName: 'failed.pdf', status: 'Failed', chunkCount: 0, lastError: 'embedding timeout', createdAt: '2026-10-27T00:00:00Z', updatedAt: '2026-10-27T00:01:00Z', elapsedSeconds: 60 }] })
    if (/\/api\/knowledge-bases\/kb1\/documents\/d2\/(retry|reindex|cancel)$/.test(path)) return route.fulfill({ status: 202, json: {} })
    if (path === '/api/knowledge-bases/kb1/documents/d1/file') return route.fulfill({ status: 200, contentType: 'application/pdf', body: '%PDF-test' })
    if (path === '/api/conversations' && method === 'GET') {
      const wantsArchived = url.searchParams.get('archived') === 'true'
      const item = { id: 'c1', knowledgeBaseId: 'kb1', title, archived, createdAt: '2026-10-27T00:00:00Z', updatedAt: '2026-10-27T00:00:00Z' }
      return route.fulfill({ json: wantsArchived === archived ? [item] : [] })
    }
    if (path === '/api/conversations/c1' && method === 'PATCH') {
      title = (JSON.parse(route.request().postData() || '{}') as { title?: string }).title || title
      return route.fulfill({ json: { id: 'c1', knowledgeBaseId: 'kb1', title, createdAt: '2026-10-27T00:00:00Z', updatedAt: '2026-10-27T00:00:02Z' } })
    }
    if (path === '/api/conversations/c1/archive' && method === 'POST') {
      archived = true
      return route.fulfill({ status: 204 })
    }
    if (path === '/api/conversations/c1/archive' && method === 'DELETE') {
      archived = false
      return route.fulfill({ status: 204 })
    }
    if (path === '/api/conversations/c1/messages/page') {
      return route.fulfill({ json: streamed ? { messages: [{ id: 'm2', role: 'assistant', content: 'The **policy** answer [1].\n\n- Grounded in the handbook', sourcesJson: JSON.stringify([source]), createdAt: '2026-10-27T00:00:00.1230000+00:00' }, { id: 'm1', role: 'user', content: 'What is the policy?', createdAt: '2026-10-27T00:00:00.1230000+00:00' }], hasEarlier: false, previousCursor: null } : { messages: [], hasEarlier: false, previousCursor: null } })
    }
    if (path === '/api/conversations/c1/messages/m2/feedback') return route.fulfill({ status: 204 })
    if (path === '/api/conversations/c1/messages/stream') {
      streamed = true
      const progress = (sequence: number, stepId: string, stage: string, type: string, actor: string, label: string, status: string, data: Record<string, unknown> = {}, elapsedMs?: number) =>
        `event: progress\ndata: ${JSON.stringify({ runId: 'run-test', sequence, stepId, stage, type, actor, label, status, data, elapsedMs })}\n\n`
      const body = [
        progress(1, 'workflow', 'workflow', 'workflow.started', 'KnowledgeRagWorkflow', '开始处理问题', 'running'),
        progress(2, 'planner', 'planner', 'agent.started', 'QueryPlannerAgent', '正在分析问题并规划检索', 'running'),
        progress(3, 'planner', 'planner', 'agent.completed', 'QueryPlannerAgent', '问题分析完成', 'completed', { intent: 'answer', queryCount: 2, requiresKnowledge: true }, 120),
        progress(4, 'retrieval', 'retrieval', 'stage.started', 'MultiQuerySearchService', '正在并行检索知识库', 'running', { queryCount: 2 }),
        progress(5, 'query-1', 'retrieval', 'query.completed', 'KnowledgeSearchService', '检索完成（4 条）', 'completed', { query: 'policy', resultCount: 4 }, 80),
        progress(6, 'retrieval', 'retrieval', 'stage.completed', 'MultiQuerySearchService', '检索、融合与重排完成', 'completed', { evidenceCount: 4 }, 210),
        progress(7, 'review-1', 'review', 'agent.completed', 'EvidenceReviewerAgent', '证据审核通过', 'completed', { supported: true, confidence: .93, issueCount: 0 }, 90),
        progress(8, 'workflow', 'workflow', 'workflow.completed', 'KnowledgeRagWorkflow', '回答已完成', 'completed', { reviewPassed: true, evidenceCount: 4 }, 580),
        'event: delta\ndata: {"text":"The **policy** answer [1].\\n\\n- Grounded in the handbook"}\n\n',
        `event: complete\ndata: ${JSON.stringify({ conversationId: 'c1', answer: 'The **policy** answer [1].\n\n- Grounded in the handbook', sources: [source] })}\n\n`,
      ].join('')
      return route.fulfill({ status: 200, contentType: 'text/event-stream', body })
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

test('official assistant-ui Thread renders ordered turns, in-message workflow, citations and evidence', async ({ page }) => {
  await page.goto('/chat')
  const isDesktop = await page.evaluate(() => window.innerWidth >= 1024)
  const thread = page.locator('.aui-thread-root')
  const composer = page.getByRole('textbox', { name: 'Message input' })

  await expect(thread).toBeVisible()
  await expect(page.locator('[data-slot="aui_composer-shell"]')).toBeVisible()
  await expect(composer).toHaveAttribute('placeholder', 'Send a message...')
  await expect(composer).toBeInViewport()
  if (isDesktop) {
    await expect(page.getByTestId('conversation-panel')).toBeVisible()
    await expect(page.getByTestId('source-panel')).toHaveCount(0)
  }

  await composer.fill('What is the policy?')
  await page.getByRole('button', { name: 'Send message' }).click()
  await expect(page.getByText('policy', { exact: true }).last()).toBeVisible()
  await expect(page.getByText('Grounded in the handbook')).toBeVisible()

  const messageRoots = page.locator('[data-slot="aui_message-group"] [data-role]')
  await expect(messageRoots).toHaveCount(2)
  expect(await messageRoots.evaluateAll((elements) => elements.map((element) => element.getAttribute('data-role')))).toEqual(['user', 'assistant'])

  const assistantMessage = page.locator('[data-slot="aui_assistant-message-root"]')
  await expect(assistantMessage.getByText('Agent workflow · 完成', { exact: false })).toBeVisible()
  await expect(assistantMessage).toContainText('回答已完成')
  await expect(page.getByTestId('run-status-bar')).toHaveCount(0)

  const sourceChip = page.locator('a[data-slot="source"][href="#kr-source-1-m2"]')
  const inlineCitation = page.locator('a[href="#kr-citation-1-m2"]')
  await expect(sourceChip).toContainText('[1] handbook.pdf')
  await expect(inlineCitation).toHaveText('1')

  const feedbackRequest = page.waitForRequest((request) => request.url().endsWith('/api/conversations/c1/messages/m2/feedback'))
  await page.getByRole('button', { name: 'Helpful', exact: true }).click()
  await feedbackRequest

  await sourceChip.click()
  if (isDesktop) await expect(page.getByTestId('source-panel')).toBeVisible()
  else await expect(page.locator('div.fixed.inset-0.z-50.lg\\:hidden').filter({ hasText: '来源证据' })).toBeVisible()
  await expect(page.getByText('Employees receive the documented benefit.', { exact: false }).last()).toBeVisible()

  if (isDesktop) {
    await page.getByTestId('source-panel').getByRole('button', { name: '关闭来源证据' }).click()
    await expect(page.getByTestId('source-panel')).toHaveCount(0)
    await inlineCitation.click()
    await expect(page.getByTestId('source-panel')).toBeVisible()
  }

  const documentScrolls = await page.evaluate(() => document.documentElement.scrollHeight > window.innerHeight + 2)
  expect(documentScrolls).toBeFalsy()
  await expect(composer).toBeInViewport()

  const activeSource = isDesktop
    ? page.getByTestId('source-panel')
    : page.locator('div.fixed.inset-0.z-50.lg\\:hidden').filter({ hasText: '来源证据' })
  const openDocument = activeSource.getByRole('button', { name: '打开原始文档' })
  await openDocument.scrollIntoViewIfNeeded()
  await expect(openDocument).toBeInViewport()

  const sourceRequest = page.waitForRequest((request) => request.url().endsWith('/api/knowledge-bases/kb1/documents/d1/file'))
  await openDocument.click()
  await sourceRequest

  if (isDesktop) {
    await page.getByRole('button', { name: '进入专注模式' }).click()
    await expect(page.getByTestId('conversation-panel')).toHaveCount(0)
    await expect(page.getByTestId('source-panel')).toHaveCount(0)
    await expect(page.getByRole('button', { name: '退出专注模式' })).toBeVisible()
    await expect(composer).toBeInViewport()
  }
})

test('official assistant-ui ThreadList can rename, archive and restore a conversation', async ({ page }) => {
  await page.goto('/chat')
  const isDesktop = await page.evaluate(() => window.innerWidth >= 1024)
  if (!isDesktop) return

  await expect(page.locator('[data-slot="aui_thread-list-root"]')).toBeVisible()
  await expect(page.getByRole('searchbox', { name: 'Search threads' })).toBeVisible()

  await page.getByRole('button', { name: 'More options' }).click()
  await page.getByText('Rename', { exact: true }).click()
  const renameInput = page.getByRole('textbox', { name: 'Rename thread' })
  await renameInput.fill('Renamed policy')
  const renameRequest = page.waitForRequest((request) => request.url().endsWith('/api/conversations/c1') && request.method() === 'PATCH')
  await renameInput.press('Enter')
  await renameRequest
  const threadList = page.locator('[data-slot="aui_thread-list-root"]')
  let renamedThread = threadList.locator('[data-slot="aui_thread-list-item"]').filter({ hasText: 'Renamed policy' })
  await expect(renamedThread).toBeVisible()

  await page.reload()
  const stableThreadList = page.locator('[data-slot="aui_thread-list-root"]')
  await expect(stableThreadList).toBeVisible()
  renamedThread = stableThreadList.locator('[data-slot="aui_thread-list-item"]').filter({ hasText: 'Renamed policy' })
  await expect(renamedThread).toBeVisible()
  await renamedThread.hover()
  await renamedThread.getByRole('button', { name: 'More options' }).click()
  await expect(page.locator('[data-slot="aui_thread-list-item-more-content"][data-state="open"]')).toBeVisible()
  const archiveRequest = page.waitForRequest((request) => request.url().endsWith('/api/conversations/c1/archive') && request.method() === 'POST')
  await page.getByRole('menuitem', { name: 'Archive', exact: true }).click()
  await archiveRequest
  await expect(page.getByText('Archived')).toBeVisible()
  await expect(page.getByRole('button', { name: '恢复会话' })).toBeVisible()

  const restoreRequest = page.waitForRequest((request) => request.url().endsWith('/api/conversations/c1/archive') && request.method() === 'DELETE')
  await page.getByRole('button', { name: '恢复会话' }).click()
  await restoreRequest
  await expect(page.getByRole('button', { name: 'Renamed policy', exact: true })).toBeVisible()
})