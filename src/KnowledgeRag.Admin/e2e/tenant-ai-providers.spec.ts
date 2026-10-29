import { expect, test, type Page } from '@playwright/test'

const session = {
  accessToken: 'test-access-token',
  refreshToken: 'test-refresh-token',
  expiresAt: '2099-01-01T00:00:00Z',
  refreshExpiresAt: '2099-02-01T00:00:00Z',
  user: { id: 'u1', email: 'owner@example.com', displayName: 'Owner' },
}

async function authenticate(page: Page) {
  await page.addInitScript((value) => localStorage.setItem('knowledge-rag-admin-session', JSON.stringify(value)), session)
}

test('tenant owner can configure an AI provider without exposing the credential', async ({ page }) => {
  await authenticate(page)
  const connections: Array<Record<string, unknown>> = []
  let savedCredential = ''

  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url())
    const path = url.pathname
    const method = route.request().method()
    if (path === '/api/tenants' && method === 'GET') return route.fulfill({ json: [{ id: 't1', name: 'Acme', slug: 'acme', role: 'Owner', createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' }] })
    if (path === '/api/tenants/ai/providers') return route.fulfill({ json: [{ id: 'openai-compatible', name: 'OpenAI Compatible', description: 'Compatible provider', supportsCustomBaseUrl: true }] })
    if (path === '/api/tenants/t1/ai/connections' && method === 'GET') return route.fulfill({ json: connections })
    if (path === '/api/tenants/t1/ai/connections' && method === 'POST') {
      const body = JSON.parse(route.request().postData() || '{}') as Record<string, unknown>
      savedCredential = String(body.credential || '')
      const result = { id: 'conn1', tenantId: 't1', name: body.name, providerType: body.providerType, baseUrl: body.baseUrl, hasCredential: true, isEnabled: true, revision: 1, updatedAt: '2026-10-28T00:00:00Z' }
      connections.splice(0, connections.length, result)
      return route.fulfill({ json: result })
    }
    if (path === '/api/tenants/t1/ai/deployments') return route.fulfill({ json: [] })
    if (path === '/api/tenants/t1/ai/defaults') return route.fulfill({ json: { tenantId: 't1', defaultChatDeploymentId: null, defaultEmbeddingDeploymentId: null, defaultRerankerDeploymentId: null, updatedAt: null } })
    return route.fulfill({ status: 200, json: [] })
  })

  await page.goto('/tenants')
  await page.getByRole('button', { name: 'AI Providers' }).click()
  await expect(page.getByText('Tenant AI Control Plane')).toBeVisible()
  await expect(page.getByText('还没有 Tenant AI Provider Connection。', { exact: false })).toBeVisible()

  await page.getByRole('button', { name: '添加连接' }).click()
  await expect(page.getByLabel('名称')).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Provider' })).toBeVisible()
  await expect(page.getByLabel('Base URL')).toBeVisible()
  await expect(page.getByLabel('API Key / Credential')).toBeVisible()
  await page.getByLabel('名称').fill('OpenAI Production')
  await page.getByLabel('Base URL').fill('https://api.openai.com/v1')
  await page.getByLabel('API Key / Credential').fill('super-secret-provider-key')
  await page.getByRole('button', { name: '保存', exact: true }).click()

  await expect(page.getByText('OpenAI Production')).toBeVisible()
  expect(savedCredential).toBe('super-secret-provider-key')
  await expect(page.getByText('super-secret-provider-key')).toHaveCount(0)
  await expect(page.getByText('Credential configured')).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Chat' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Embedding target' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Reranker' })).toBeVisible()

  await page.getByRole('button', { name: '添加模型' }).click()
  await expect(page.getByRole('combobox', { name: 'Provider Connection' })).toBeVisible()
  await expect(page.getByLabel('显示名称')).toBeVisible()
  await expect(page.getByLabel('远端模型名')).toBeVisible()
  await expect(page.getByRole('combobox', { name: '类型' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Fallback deployment' })).toBeVisible()
  await page.getByRole('combobox', { name: '类型' }).click()
  await page.getByRole('option', { name: 'Embedding', exact: true }).click()
  await expect(page.getByLabel('Embedding dimensions')).toBeVisible()
  await page.getByRole('button', { name: '取消' }).click()
})
