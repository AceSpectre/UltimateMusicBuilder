import { test, expect, type ElectronApplication } from '@playwright/test'
import { _electron as electron } from '@playwright/test'
import { rmSync, existsSync } from 'fs'
import { resolve, join, dirname } from 'path'
import { fileURLToPath } from 'url'
import { firstWindow, closeApp, hasGameResources, hasTool } from './e2e-utils'
import { prepareIsolatedBuild, runCliBuild, snapshot, type IsolatedBuild } from './build-harness'
import { compareDirs, formatReport } from './baseline-compare'

const __dirname = dirname(fileURLToPath(import.meta.url))
let app: ElectronApplication
let build: IsolatedBuild
let refDir: string

test.describe.configure({ timeout: 600_000 }) // two real builds

test.beforeAll(async () => {
  test.skip(!hasGameResources() || !hasTool('dotnet'), 'requires local game resources + dotnet')
  build = prepareIsolatedBuild()

  runCliBuild(build)
  // 'Ref': avoids cpSync's copy-into-self guard on the ArcOutput prefix
  refDir = join(build.wsRoot, 'Ref')
  snapshot(build.arcOutput, refDir)
  rmSync(build.arcOutput, { recursive: true, force: true })

  const mainPath = resolve(__dirname, '..', 'dist', 'main', 'index.js')
  app = await electron.launch({
    args: [mainPath],
    env: { ...process.env, UMB_WORKSPACE: build.wsRoot, NODE_ENV: 'test' }
  })
})

test.afterAll(async () => {
  await closeApp(app)
  // retry once: dotnet may still lock the temp dir
  try {
    build?.cleanup()
  } catch {
    await new Promise((r) => setTimeout(r, 3000))
    try { build?.cleanup() } catch { /* ignore */ }
  }
})

test('desktop build output is byte-identical to the CLI build', async () => {
  const page = await firstWindow(app)
  await page.evaluate(() => window.electron.umb.runAction('build'))

  expect(existsSync(join(build.arcOutput, 'ui', 'param', 'database', 'ui_bgm_db.prc'))).toBe(true)

  const report = compareDirs(refDir, build.arcOutput)
  expect(report.isClean, formatReport(report)).toBe(true)
})
