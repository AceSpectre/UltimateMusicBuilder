import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: 'e2e',
  // Refuses to run against a stale/missing dist/ build. See e2e/global-setup.ts.
  globalSetup: './e2e/global-setup.ts',
  timeout: 120_000,
  retries: 0,
  workers: 1,
  reporter: [['list']],
  use: {
    trace: 'on-first-retry'
  }
})
