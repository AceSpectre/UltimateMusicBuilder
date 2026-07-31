import { existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from 'fs'
import { isAbsolute, dirname, join, relative, resolve } from 'path'
import { tmpdir } from 'os'
import type { LogLine } from '../shared/types'

export function nowTs(): string {
  return new Date().toLocaleTimeString('en-GB', { hour12: false })
}

export function log(level: LogLine['level'], message: string): LogLine {
  return { timestamp: nowTs(), level, message }
}

export function isChildOf(parentPath: string, childPath: string): boolean {
  const rel = relative(parentPath, childPath)
  return rel !== '' && !rel.startsWith('..') && !isAbsolute(rel)
}

export function getMusicModsRoot(workspace: string): string {
  return resolve(workspace, 'Mods', 'MusicMods')
}

/** Resolves a renderer-supplied path and rejects anything outside Mods/MusicMods. */
export function resolveUnderMods(workspace: string, path: string, message = 'Invalid mod path.'): string {
  const resolved = resolve(path)
  if (!isChildOf(getMusicModsRoot(workspace), resolved)) {
    throw new Error(message)
  }
  return resolved
}

export function readJson<T>(path: string): T | null {
  try {
    if (!existsSync(path)) return null
    return JSON.parse(readFileSync(path, 'utf-8')) as T
  } catch {
    return null
  }
}

export function writeJson(path: string, value: unknown): void {
  try {
    const dir = dirname(path)
    if (!existsSync(dir)) mkdirSync(dir, { recursive: true })
    writeFileSync(path, JSON.stringify(value, null, 2), 'utf-8')
  } catch {
    /* best-effort cache; ignore failures */
  }
}

export function statSafe(path: string): { mtimeMs: number; size: number } | null {
  try {
    const s = statSync(path)
    return { mtimeMs: s.mtimeMs, size: s.size }
  } catch {
    return null
  }
}

export function tempPath(prefix: string, ext: string): string {
  return join(tmpdir(), `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1e6)}.${ext}`)
}

/**
 * Incremental newline splitter for child-process stdout/stderr streams.
 * push() feeds raw chunks and emits complete lines; flush() emits the tail.
 */
export function lineSplitter(onLine: (line: string) => void): { push: (data: Buffer) => void; flush: () => void } {
  let buffer = ''
  return {
    push(data: Buffer): void {
      buffer += data.toString()
      const lines = buffer.split('\n')
      buffer = lines.pop() || ''
      for (const line of lines) onLine(line)
    },
    flush(): void {
      if (buffer.trim()) onLine(buffer)
      buffer = ''
    }
  }
}
