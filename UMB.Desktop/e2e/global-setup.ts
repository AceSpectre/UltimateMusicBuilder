import { existsSync, readdirSync, statSync } from 'fs'
import { join, resolve, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const root = resolve(__dirname, '..')

/**
 * The E2E suite launches the compiled app out of `dist/`, so a stale build means
 * the tests silently pass or fail against code that is no longer on disk.
 * `pretest:e2e` covers `npm run test:e2e`, but a bare `npx playwright test` skips
 * that hook, so the check lives here where every entrypoint has to pass through it.
 */

/** Newest mtime under `dir`, ignoring the usual generated/dependency trees. */
function newestMtime(dir: string, skip: ReadonlySet<string> = new Set()): number {
  let newest = 0
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (skip.has(entry.name)) continue
    const full = join(dir, entry.name)
    newest = Math.max(newest, entry.isDirectory() ? newestMtime(full, skip) : statSync(full).mtimeMs)
  }
  return newest
}

export default function globalSetup(): void {
  const mainBundle = join(root, 'dist', 'main', 'index.js')
  if (!existsSync(mainBundle)) {
    throw new Error(
      `E2E: no build found at ${mainBundle}.\nRun \`npm run build\` (or \`npm run test:e2e\`, which builds first).`
    )
  }

  const builtAt = newestMtime(join(root, 'dist'))
  // The build inputs that actually end up in the bundle. Config files are included
  // because a changed vite/electron config changes the output too.
  const sources = Math.max(
    newestMtime(join(root, 'src')),
    ...['electron.vite.config.ts', 'package.json', 'tailwind.config.js', 'postcss.config.js']
      .map((f) => join(root, f))
      .filter(existsSync)
      .map((f) => statSync(f).mtimeMs)
  )

  if (sources > builtAt) {
    const staleBy = Math.round((sources - builtAt) / 1000)
    throw new Error(
      `E2E: dist/ is stale — sources changed ${staleBy}s after the last build.\n` +
        `The suite runs the compiled app, so this would test code that no longer exists.\n` +
        `Run \`npm run build\` (or \`npm run test:e2e\`, which builds first).`
    )
  }
}
