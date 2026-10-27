import { expect, test } from '@playwright/test'

test('sign-in page renders as a product surface', async ({ page }) => {
  await page.goto('/sign-in')
  await expect(page.getByRole('heading', { name: '欢迎回来' })).toBeVisible()
  await expect(page.getByRole('button', { name: /进入工作空间/ })).toBeVisible()
  await expect(page.getByLabel('邮箱')).toBeVisible()
  await expect(page.getByLabel('密码')).toBeVisible()
  await expect(page.locator('body')).not.toHaveCSS('overflow-x', 'scroll')
})

test('register navigation works without backend access', async ({ page }) => {
  await page.goto('/sign-in')
  await page.getByRole('button', { name: '创建账号' }).click()
  await expect(page).toHaveURL(/\/register$/)
  await expect(page.getByRole('heading', { name: '创建你的工作空间' })).toBeVisible()
  await expect(page.getByRole('button', { name: /创建并进入/ })).toBeVisible()
})
