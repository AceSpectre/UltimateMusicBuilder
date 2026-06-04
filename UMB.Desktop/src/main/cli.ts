import { spawn, type ChildProcess } from 'child_process'
import { join } from 'path'
import { app } from 'electron'

let currentProcess: ChildProcess | null = null

export interface LogLine {
  timestamp: string
  level: 'info' | 'warn' | 'error'
  message: string
}

function nowTs(): string {
  return new Date().toLocaleTimeString('en-GB', { hour12: false })
}

function parseLogLine(raw: string): LogLine {
  const timestamp = nowTs()

  // .NET's console logger prefixes each line with its level: "info:", "warn:", "fail:"
  // (and "crit:"). Classify on that prefix — scanning the whole line for the word
  // "error"/"warning" misclassifies success messages like "...check for any error."
  const level = raw.match(/^\s*(info|warn|fail|crit|dbug|trce)\s*:/i)?.[1]?.toLowerCase()
  if (level === 'warn') return { timestamp, level: 'warn', message: raw.trim() }
  if (level === 'fail' || level === 'crit') return { timestamp, level: 'error', message: raw.trim() }
  if (level) return { timestamp, level: 'info', message: raw.trim() }

  // No recognized prefix — fall back to bracket tags then a substring scan.
  if (raw.includes('[Warning]') || raw.includes('[WRN]')) {
    return { timestamp, level: 'warn', message: raw.replace(/\[Warning\]|\[WRN\]\s*/g, '').trim() }
  }
  if (raw.includes('[Error]') || raw.includes('[ERR]')) {
    return { timestamp, level: 'error', message: raw.replace(/\[Error\]|\[ERR\]\s*/g, '').trim() }
  }
  return { timestamp, level: 'info', message: raw.trim() }
}

/** Resolves the CLI command + arg list for the current package mode. */
function cliInvocation(workspace: string, trailing: string[]): { command: string; args: string[] } {
  if (app.isPackaged) {
    return { command: join(process.resourcesPath, 'cli', 'UMB.CLI.exe'), args: trailing }
  }
  return {
    command: 'dotnet',
    args: ['run', '--project', join(workspace, 'UMB.CLI'), '--no-launch-profile', '--', ...trailing]
  }
}

// Headless, stateless batch actions are routed through the persistent daemon so
// they don't pay process-spawn + .NET/DI bootstrap on every call. Window-opening
// (config-volume, nus3-convert) and heavy actions (build, …) stay one-shot.
const DAEMON_ACTIONS = new Set([
  'config-volume-analyze',
  'config-volume-save',
  'config-volume-preview',
  'nus3-convert-batch',
  'accept-nus3-batch'
])

export function spawnCliAction(
  workspace: string,
  action: string,
  args: string[],
  onLine: (line: LogLine) => void
): Promise<number> {
  if (DAEMON_ACTIONS.has(action)) return daemonRequest(workspace, action, args, onLine)
  return spawnOneShot(workspace, action, args, onLine)
}

// ── One-shot spawn (window / heavy actions) ──

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
    const { command, args: spawnArgs } = cliInvocation(workspace, [action, ...args])

    const proc = spawn(command, spawnArgs, {
      cwd: workspace,
      env: { ...process.env },
      stdio: ['pipe', 'pipe', 'pipe']
    })

    currentProcess = proc

    let stdoutBuffer = ''
    let stderrBuffer = ''

    proc.stdout?.on('data', (data: Buffer) => {
      stdoutBuffer += data.toString()
      const lines = stdoutBuffer.split('\n')
      stdoutBuffer = lines.pop() || ''
      for (const line of lines) {
        if (line.trim()) onLine(parseLogLine(line))
      }
    })

    proc.stderr?.on('data', (data: Buffer) => {
      stderrBuffer += data.toString()
      const lines = stderrBuffer.split('\n')
      stderrBuffer = lines.pop() || ''
      for (const line of lines) {
        if (line.trim()) onLine({ ...parseLogLine(line), level: 'error' })
      }
    })

    proc.on('close', (code, signal) => {
      if (stdoutBuffer.trim()) onLine(parseLogLine(stdoutBuffer))
      if (stderrBuffer.trim()) onLine({ ...parseLogLine(stderrBuffer), level: 'error' })
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

// ── Persistent daemon (headless batch actions) ──

let daemonProcess: ChildProcess | null = null
let daemonStdout = ''
let daemonStderr = ''
let nextReqId = 1

// The single in-flight request. Requests are serialized through `daemonQueue`,
// so there is at most one active at any time and stdout lines map unambiguously.
let activeReq: { id: number; onLine: (line: LogLine) => void; resolve: (code: number) => void } | null = null

// Serial queue: one stdin pipe means one request at a time. This matches the
// renderer's stale-response guard and the previous single-flight model.
let daemonQueue: Promise<unknown> = Promise.resolve()

const DONE_RE = /^__DONE__\t(\d+)\t(-?\d+)$/

function daemonRequest(
  workspace: string,
  action: string,
  args: string[],
  onLine: (line: LogLine) => void
): Promise<number> {
  const start = (): Promise<number> => sendDaemonRequest(workspace, action, args, onLine)
  const result = daemonQueue.then(start, start)
  daemonQueue = result.catch(() => {}) // a failed request must not break the chain
  return result
}

function ensureDaemon(workspace: string): ChildProcess | null {
  if (daemonProcess) return daemonProcess

  const { command, args } = cliInvocation(workspace, ['serve'])
  const proc = spawn(command, args, {
    cwd: workspace,
    env: { ...process.env },
    stdio: ['pipe', 'pipe', 'pipe']
  })
  daemonProcess = proc
  daemonStdout = ''
  daemonStderr = ''

  proc.stdout?.on('data', (data: Buffer) => {
    daemonStdout += data.toString()
    const lines = daemonStdout.split('\n')
    daemonStdout = lines.pop() || ''
    for (const line of lines) handleDaemonLine(line)
  })

  proc.stderr?.on('data', (data: Buffer) => {
    daemonStderr += data.toString()
    const lines = daemonStderr.split('\n')
    daemonStderr = lines.pop() || ''
    for (const line of lines) {
      if (line.trim() && activeReq) activeReq.onLine({ ...parseLogLine(line), level: 'error' })
    }
  })

  const onExit = (code: number | null): void => {
    daemonProcess = null
    if (activeReq) {
      activeReq.onLine({
        timestamp: nowTs(),
        level: 'warn',
        message: `CLI daemon exited (${code ?? 'terminated'}); it will restart on the next request.`
      })
      const req = activeReq
      activeReq = null
      req.resolve(code ?? -1)
    }
  }

  proc.on('close', onExit)
  proc.on('error', (err) => {
    if (activeReq) {
      activeReq.onLine({ timestamp: nowTs(), level: 'error', message: `CLI daemon error: ${err.message}` })
    }
    onExit(-1)
  })

  return daemonProcess
}

function handleDaemonLine(rawLine: string): void {
  // .NET WriteLine emits CRLF on Windows; splitting on '\n' leaves a trailing '\r'
  // that would otherwise break the sentinel match (and the request would hang).
  const line = rawLine.replace(/\r$/, '')
  const done = line.match(DONE_RE)
  if (done) {
    const code = parseInt(done[2], 10)
    if (activeReq) {
      const req = activeReq
      activeReq = null
      req.resolve(code)
    }
    return
  }
  if (line.trim() && activeReq) activeReq.onLine(parseLogLine(line))
}

function sendDaemonRequest(
  workspace: string,
  action: string,
  args: string[],
  onLine: (line: LogLine) => void
): Promise<number> {
  return new Promise<number>((resolve) => {
    const proc = ensureDaemon(workspace)
    if (!proc || !proc.stdin) {
      onLine({ timestamp: nowTs(), level: 'error', message: 'CLI daemon unavailable.' })
      resolve(-1)
      return
    }
    const id = nextReqId++
    activeReq = { id, onLine, resolve }
    proc.stdin.write(`${JSON.stringify({ id, action, args })}\n`)
  })
}

/** Stops the persistent daemon (call on app quit). */
export function shutdownDaemon(): void {
  if (!daemonProcess) return
  try {
    daemonProcess.stdin?.write(`${JSON.stringify({ id: 0, action: '__shutdown__', args: [] })}\n`)
  } catch {
    /* ignore */
  }
  try {
    daemonProcess.kill()
  } catch {
    /* ignore */
  }
  daemonProcess = null
}
