import { spawn, type ChildProcess } from 'child_process'
import { existsSync, unlinkSync, writeFileSync } from 'fs'
import { join, resolve } from 'path'
import { app } from 'electron'
import { lineSplitter, nowTs, readJson, tempPath } from './utils'
import type { LogLine } from '../shared/types'

export type { LogLine } from '../shared/types'

let currentProcess: ChildProcess | null = null

function parseLogLine(raw: string): LogLine {
  const timestamp = nowTs()

  // .NET console logger prefixes each line with its level: "info:", "warn:", "fail:", "crit:".
  const level = raw.match(/^\s*(info|warn|fail|crit|dbug|trce)\s*:/i)?.[1]?.toLowerCase()
  if (level === 'warn') return { timestamp, level: 'warn', message: raw.trim() }
  if (level === 'fail' || level === 'crit') return { timestamp, level: 'error', message: raw.trim() }
  if (level) return { timestamp, level: 'info', message: raw.trim() }

  if (raw.includes('[Warning]') || raw.includes('[WRN]')) {
    return { timestamp, level: 'warn', message: raw.replace(/\[Warning\]|\[WRN\]\s*/g, '').trim() }
  }
  if (raw.includes('[Error]') || raw.includes('[ERR]')) {
    return { timestamp, level: 'error', message: raw.replace(/\[Error\]|\[ERR\]\s*/g, '').trim() }
  }
  return { timestamp, level: 'info', message: raw.trim() }
}

export function packagedCliPath(
  resourcesPath: string,
  platform: NodeJS.Platform = process.platform
): string {
  return join(resourcesPath, 'cli', platform === 'win32' ? 'UMB.CLI.exe' : 'UMB.CLI')
}

/**
 * Resolves the CLI command + arg list for the current package mode. In dev it runs the CLI
 * built by `npm run build:cli`: the workspace's own UMB.CLI when it has one (as the repo
 * does), otherwise the app's repo (dist/main → repo root), so any folder can be the workspace.
 */
function cliInvocation(workspace: string, trailing: string[]): { command: string; args: string[] } {
  if (app.isPackaged) {
    return { command: packagedCliPath(process.resourcesPath), args: trailing }
  }
  const workspaceCli = join(workspace, 'UMB.CLI')
  const project = existsSync(workspaceCli) ? workspaceCli : resolve(__dirname, '..', '..', '..', 'UMB.CLI')
  return { command: 'dotnet', args: [join(project, 'bin', 'Debug', 'net8.0', 'UMB.CLI.dll'), ...trailing] }
}

function spawnCli(workspace: string, trailing: string[]): ChildProcess {
  const { command, args } = cliInvocation(workspace, trailing)
  return spawn(command, args, {
    cwd: workspace,
    // UMB_WORKSPACE anchors the CLI's relative paths (Mods/, Resources/, ArcOutput/) to the shared workspace root.
    env: { ...process.env, UMB_WORKSPACE: workspace },
    stdio: ['pipe', 'pipe', 'pipe']
  })
}

// Long-running actions get their own daemon so they don't hold up quick, interactive ones
// (a loop analysis can take a minute; a loop preview should not wait for it).
const BACKGROUND_ACTIONS = new Set([
  'config-volume-analyze',
  'nus3-analyze-loop',
  'nus3-convert-track',
  'nus3-accept',
  'merge-execute',
  'extract-icons-run'
])

// Headless batch actions go through a persistent daemon so they don't pay process-spawn +
// .NET/DI bootstrap on every call. Window-opening and heavy actions (build, …) stay one-shot.
const DAEMON_ACTIONS = new Set([
  ...BACKGROUND_ACTIONS,
  'config-volume-save',
  'config-volume-preview'
])

export function spawnCliAction(
  workspace: string,
  action: string,
  args: string[],
  onLine: (line: LogLine) => void
): Promise<number> {
  if (DAEMON_ACTIONS.has(action)) return daemonFor(action).request(workspace, action, args, onLine)
  return spawnOneShot(workspace, action, args, onLine)
}

/**
 * Calls a JSON action on the CLI (UMB.CLI/Desktop/DesktopApi.cs): writes the input, runs the
 * action through the daemon and returns its result, throwing its error message on failure.
 */
export async function callCli<T>(
  workspace: string,
  action: string,
  input: unknown,
  onLine: (line: LogLine) => void = () => {}
): Promise<T> {
  const inputPath = tempPath('umb-in', 'json')
  const outputPath = tempPath('umb-out', 'json')
  writeFileSync(inputPath, JSON.stringify(input ?? {}), 'utf-8')
  try {
    const code = await daemonFor(action).request(workspace, action, [inputPath, outputPath], onLine)
    const response = readJson<{ result?: T; error?: string }>(outputPath)
    if (!response) throw new Error(`CLI action '${action}' produced no result (exit code ${code}).`)
    if (response.error !== undefined) throw new Error(response.error)
    return response.result as T
  } finally {
    for (const path of [inputPath, outputPath]) {
      try { if (existsSync(path)) unlinkSync(path) } catch { /* ignore */ }
    }
  }
}

function spawnOneShot(
  workspace: string,
  action: string,
  args: string[],
  onLine: (line: LogLine) => void
): Promise<number> {
  if (currentProcess) {
    onLine({
      timestamp: nowTs(),
      level: 'warn',
      message: 'Another CLI action is already running. Cancel it before starting a new one.'
    })
    return Promise.resolve(-1)
  }

  return new Promise<number>((resolveExit) => {
    const proc = spawnCli(workspace, [action, ...args])
    currentProcess = proc

    const stdout = lineSplitter((line) => {
      if (line.trim()) onLine(parseLogLine(line))
    })
    const stderr = lineSplitter((line) => {
      if (line.trim()) onLine({ ...parseLogLine(line), level: 'error' })
    })

    proc.stdout?.on('data', stdout.push)
    proc.stderr?.on('data', stderr.push)

    proc.on('close', (code, signal) => {
      stdout.flush()
      stderr.flush()
      onLine({
        timestamp: nowTs(),
        level: code === 0 ? 'info' : code === null ? 'warn' : 'error',
        message: code === null
          ? `Process exited before completion (${signal ?? 'terminated'})`
          : `Process exited with code ${code}`
      })
      currentProcess = null
      resolveExit(code ?? -1)
    })
  })
}

export function cancelCurrentAction(): void {
  if (currentProcess) {
    currentProcess.kill()
    currentProcess = null
  }
}

const DONE_RE = /^__DONE__\t(\d+)\t(-?\d+)$/

// Request ids are unique across daemons and restarts.
let nextReqId = 1

/** One persistent `UMB.CLI serve` process; requests are sent one at a time over stdin. */
class Daemon {
  private process: ChildProcess | null = null
  // The single in-flight request; serialised through `queue`, so stdout lines map unambiguously.
  private active: { onLine: (line: LogLine) => void; resolve: (code: number) => void } | null = null
  private queue: Promise<unknown> = Promise.resolve()

  request(workspace: string, action: string, args: string[], onLine: (line: LogLine) => void): Promise<number> {
    const start = (): Promise<number> => this.send(workspace, action, args, onLine)
    const result = this.queue.then(start, start)
    this.queue = result.catch(() => {}) // a failed request must not break the chain
    return result
  }

  shutdown(): void {
    if (!this.process) return
    try {
      this.process.stdin?.write(`${JSON.stringify({ id: 0, action: '__shutdown__', args: [] })}\n`)
    } catch {
      /* ignore */
    }
    try {
      this.process.kill()
    } catch {
      /* ignore */
    }
    this.process = null
  }

  private send(workspace: string, action: string, args: string[], onLine: (line: LogLine) => void): Promise<number> {
    return new Promise<number>((resolve) => {
      const proc = this.ensureStarted(workspace)
      if (!proc.stdin) {
        onLine({ timestamp: nowTs(), level: 'error', message: 'CLI daemon unavailable.' })
        resolve(-1)
        return
      }
      this.active = { onLine, resolve }
      proc.stdin.write(`${JSON.stringify({ id: nextReqId++, action, args })}\n`)
    })
  }

  private ensureStarted(workspace: string): ChildProcess {
    if (this.process) return this.process

    const proc = spawnCli(workspace, ['serve'])
    this.process = proc

    const stdout = lineSplitter((line) => this.handleLine(line))
    const stderr = lineSplitter((line) => {
      if (line.trim() && this.active) this.active.onLine({ ...parseLogLine(line), level: 'error' })
    })
    proc.stdout?.on('data', stdout.push)
    proc.stderr?.on('data', stderr.push)

    const onExit = (code: number | null): void => {
      if (this.process === proc) this.process = null
      if (this.active) {
        this.active.onLine({
          timestamp: nowTs(),
          level: 'warn',
          message: `CLI daemon exited (${code ?? 'terminated'}); it will restart on the next request.`
        })
        this.finish(code ?? -1)
      }
    }
    proc.on('close', onExit)
    proc.on('error', (err) => {
      this.active?.onLine({ timestamp: nowTs(), level: 'error', message: `CLI daemon error: ${err.message}` })
      onExit(-1)
    })

    return proc
  }

  private handleLine(rawLine: string): void {
    // .NET emits CRLF; strip the trailing '\r' so the sentinel matches.
    const line = rawLine.replace(/\r$/, '')
    const done = line.match(DONE_RE)
    if (done) {
      this.finish(parseInt(done[2], 10))
      return
    }
    if (line.trim()) this.active?.onLine(parseLogLine(line))
  }

  private finish(code: number): void {
    const req = this.active
    this.active = null
    req?.resolve(code)
  }
}

const interactiveDaemon = new Daemon()
const backgroundDaemon = new Daemon()

function daemonFor(action: string): Daemon {
  return BACKGROUND_ACTIONS.has(action) ? backgroundDaemon : interactiveDaemon
}

/** Stops the persistent daemons (call on app quit). */
export function shutdownDaemon(): void {
  interactiveDaemon.shutdown()
  backgroundDaemon.shutdown()
}
