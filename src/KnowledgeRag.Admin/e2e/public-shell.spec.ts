import { expect, test } from '@playwright/test'

test('sign-in page renders as a product surface', async ({ page }) => {
  await page.goto('/sign-in')
  await expect(page.getByRole('heading', { name: /登录 Knowledge RAG/i })).toBeVisible()
  await expect(page.getByRole('button', { name: '登录' })).toBeVisible()
  await expect(page.locator('body')).not.toHaveCSS('overflow-x', 'scroll')
})

test('register navigation works without backend access', async ({ page }) => {
  await page.goto('/sign-in')
  await page.getByRole('button', { name: '注册' }).click()
  await expect(page).toHaveURL(/\/register$/)
  await expect(page.getByRole('heading', { name: /创建账号/i })).toBeVisible()
})
