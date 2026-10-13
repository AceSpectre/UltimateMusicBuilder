import { existsSync } from 'fs'
import { dirname, join } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))

/** Walks up from this file to the UltimateMusicBuilder working tree (contains Sma5h.sln). */
export function repoRoot(): string {
  let dir = __dirname
  while (true) {
    if (existsSync(join(dir, 'Sma5h.sln'))) return dir
    const parent = dirname(dir)
    if (parent === dir) throw new Error('repo root (Sma5h.sln) not found above ' + __dirname)
    dir = parent
  }
}
