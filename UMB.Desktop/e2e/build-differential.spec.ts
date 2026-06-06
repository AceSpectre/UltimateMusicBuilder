import { test, expect, type ElectronApplication } from '@playwright/test'
import { _electron as electron } from '@playwright/test'
import { rmSync, existsSync } from 'fs'
import { resolve, join, dirname } from 'path'
import { fileURLToPath } from 'url'
import { firstWindow } from './e2e-utils'
import { prepareIsolatedBuild, runCliBuild, snapshot, type IsolatedBuild } from './build-harness'
import { compareDirs, formatReport } from './baseline-compare'

const __dirname = dirname(fileURLToPath(import.meta.url))
let app: ElectronApplication
let build: IsolatedBuild
let refDir: string

test.describe.configure({ timeout: 600_000 }) // two real builds

test.beforeAll(async () => {
  build = prepareIsolatedBuild()

  // Reference: CLI build → snapshot ArcOutput → clear.
  runCliBuild(build)
  // Use 'Ref' (not 'ArcOutput-ref') so the path doesn't share the 'ArcOutput' prefix,
  // which confuses Node's cpSync "copy into self" guard on Windows.
  refDir = join(build.wsRoot, 'Ref')
  snapshot(build.arcOutput, refDir)
  rmSync(build.arcOutput, { recursive: true, force: true })

  // Actual: desktop drives the build against the SAME isolated workspace.
  const mainPath = resolve(__dirname, '..', 'dist', 'main', 'index.js')
  app = await electron.launch({
    args: [mainPath],
    env: { ...process.env, UMB_WORKSPACE: build.wsRoot, NODE_ENV: 'test' }
  })
})

test.afterAll(async () => {
  await app?.close()
  // Dotnet may still hold a short-lived lock on the copied UMB.CLI dir after the build
  // completes; retry cleanup once after a brief pause so temp files are not left on disk.
  try {
    build?.cleanup()
  } catch {
    await new Promise((r) => setTimeout(r, 3000))
    try { build?.cleanup() } catch { /* ignore residual lock on temp dir */ }
  }
})

test('desktop build output is byte-identical to the CLI build', async () => {
  const page = await firstWindow(app)
  await page.evaluate(() => window.electron.umb.runAction('build'))

  expect(existsSync(join(build.arcOutput, 'ui', 'param', 'database', 'ui_bgm_db.prc'))).toBe(true)

  const report = compareDirs(refDir, build.arcOutput)
  expect(report.isClean, formatReport(report)).toBe(true)
})
