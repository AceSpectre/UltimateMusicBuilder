import { existsSync, readFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'

export function nowTs(): string {
  return new Date().toLocaleTimeString('en-GB', { hour12: false })
}

export function readJson<T>(path: string): T | null {
  try {
    if (!existsSync(path)) return null
    return JSON.parse(readFileSync(path, 'utf-8')) as T
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
