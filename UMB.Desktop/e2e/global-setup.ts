import { existsSync, readdirSync, statSync } from 'fs'
import { join, resolve, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const root = resolve(__dirname, '..')

/** Newest mtime under `dir`. */
function newestMtime(dir: string): number {
  let newest = 0
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name)
    newest = Math.max(newest, entry.isDirectory() ? newestMtime(full) : statSync(full).mtimeMs)
  }
  return newest
}

/** Fails fast if dist/ is missing or older than src/. */
export default function globalSetup(): void {
  const mainBundle = join(root, 'dist', 'main', 'index.js')
  if (!existsSync(mainBundle)) {
    throw new Error(
      `E2E: no build found at ${mainBundle}.\nRun \`npm run build\` (or \`npm run test:e2e\`, which builds first).`
    )
  }

  const builtAt = newestMtime(join(root, 'dist'))
  // build inputs: src + configs
  const sources = Math.max(
    newestMtime(join(root, 'src')),
    ...['electron.vite.config.ts', 'package.json', 'tailwind.config.ts', 'postcss.config.js']
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
