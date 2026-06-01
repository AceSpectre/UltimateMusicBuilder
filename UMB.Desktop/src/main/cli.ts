import { spawn, type ChildProcess } from 'child_process'
import { join } from 'path'
import { app } from 'electron'

let currentProcess: ChildProcess | null = null

export interface LogLine {
  timestamp: string
  level: 'info' | 'warn' | 'error'
  message: string
}

function parseLogLine(raw: string): LogLine {
  const now = new Date()
  const timestamp = now.toLocaleTimeString('en-GB', { hour12: false })

  if (raw.includes('[Warning]') || raw.includes('[WRN]') || raw.toLowerCase().includes('warning')) {
    return { timestamp, level: 'warn', message: raw.replace(/\[Warning\]|\[WRN\]\s*/g, '').trim() }
  }
  if (raw.includes('[Error]') || raw.includes('[ERR]') || raw.toLowerCase().includes('error')) {
    return { timestamp, level: 'error', message: raw.replace(/\[Error\]|\[ERR\]\s*/g, '').trim() }
  }
  return { timestamp, level: 'info', message: raw.trim() }
}

export function spawnCliAction(
  workspace: string,
  action: string,
  args: string[],
  onLine: (line: LogLine) => void
): Promise<number> {
  if (currentProcess) {
    onLine({
      timestamp: new Date().toLocaleTimeString('en-GB', { hour12: false }),
      level: 'warn',
      message: 'Another CLI action is already running. Cancel it before starting a new one.'
    })
    return Promise.resolve(-1)
  }

  return new Promise<number>((resolveExit) => {
  let command: string
  let spawnArgs: string[]

  if (app.isPackaged) {
    command = join(process.resourcesPath, 'cli', 'UMB.CLI.exe')
    spawnArgs = [action, ...args]
  } else {
    command = 'dotnet'
    spawnArgs = ['run', '--project', join(workspace, 'UMB.CLI'), '--no-launch-profile', '--', action, ...args]
  }

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
      timestamp: new Date().toLocaleTimeString('en-GB', { hour12: false }),
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
