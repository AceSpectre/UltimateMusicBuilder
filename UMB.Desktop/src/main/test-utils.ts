import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'

export interface Workspace {
  root: string
  cleanup(): void
}

/** Creates a throwaway workspace folder under the OS temp dir. */
export function makeWorkspace(): Workspace {
  const root = mkdtempSync(join(tmpdir(), 'umb-test-'))
  return {
    root,
    cleanup: () => rmSync(root, { recursive: true, force: true })
  }
}

/** Writes a file (creating the directory if needed) and returns its full path. */
export function writeFile(dir: string, name: string, content: string | Buffer): string {
  mkdirSync(dir, { recursive: true })
  const full = join(dir, name)
  writeFileSync(full, content)
  return full
}
