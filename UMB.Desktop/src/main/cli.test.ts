import { EventEmitter } from 'events'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { LogLine } from './cli'

/**
 * cli.ts owns every path from the renderer to the CLI: log-level classification,
 * dev-vs-packaged invocation, the one-shot concurrency guard, and the persistent
 * daemon's framing/queueing. `spawn` is its only dependency, so it is faked here
 * and the module is re-imported per test because all of that state is module-level.
 */

const mocks = vi.hoisted(() => ({
  app: { isPackaged: false },
  spawn: vi.fn()
}))

vi.mock('electron', () => ({ app: mocks.app }))
vi.mock('child_process', () => ({ spawn: mocks.spawn }))

/** Minimal stand-in for the ChildProcess surface cli.ts actually touches. */
class FakeProc extends EventEmitter {
  stdout = new EventEmitter()
  stderr = new EventEmitter()
  stdin = { write: vi.fn() }
  kill = vi.fn()

  /** Feeds a raw chunk (newlines included) to stdout. */
  out(chunk: string): void {
    this.stdout.emit('data', Buffer.from(chunk))
  }

  err(chunk: string): void {
    this.stderr.emit('data', Buffer.from(chunk))
  }

  close(code: number | null, signal: string | null = null): void {
    this.emit('close', code, signal)
  }
}

const WS = '/ws'

let cli: typeof import('./cli')
let procs: FakeProc[]
let lines: LogLine[]

/** Lets queued microtasks (the daemon's serial queue) settle. */
function flush(): Promise<void> {
  return new Promise((resolve) => setImmediate(resolve))
}

function lastProc(): FakeProc {
  return procs[procs.length - 1]
}

/** Args passed to the Nth spawn call. */
function spawnCall(n = 0): { command: string; args: string[]; opts: Record<string, any> } {
  const [command, args, opts] = mocks.spawn.mock.calls[n]
  return { command, args, opts }
}

const onLine = (line: LogLine): void => {
  lines.push(line)
}

/** Levels of the lines emitted so far, ignoring the trailing "Process exited" line. */
function levels(): string[] {
  return lines.filter((l) => !l.message.startsWith('Process exited')).map((l) => l.level)
}

function messages(): string[] {
  return lines.map((l) => l.message)
}

beforeEach(async () => {
  vi.resetModules()
  mocks.spawn.mockReset()
  mocks.app.isPackaged = false
  procs = []
  lines = []
  mocks.spawn.mockImplementation(() => {
    const proc = new FakeProc()
    procs.push(proc)
    return proc
  })
  cli = await import('./cli')
})

afterEach(() => {
  // Leave no daemon behind for the next test's fresh module instance.
  cli.shutdownDaemon()
})

// ── log line classification ──

describe('log level classification', () => {
  /** Runs one CLI line through a one-shot action and returns the parsed entry. */
  async function parse(raw: string, stream: 'out' | 'err' = 'out'): Promise<LogLine> {
    const done = cli.spawnCliAction(WS, 'build', [], onLine)
    const proc = lastProc()
    proc[stream](`${raw}\n`)
    proc.close(0)
    await done
    return lines[0]
  }

  it.each([
    ['info: building', 'info'],
    ['warn: duplicate entry', 'warn'],
    ['fail: could not write', 'error'],
    ['crit: unrecoverable', 'error'],
    ['dbug: internals', 'info'],
    ['trce: verbose', 'info']
  ])('classifies the .NET "%s" prefix as %s', async (raw, level) => {
    expect((await parse(raw)).level).toBe(level)
  })

  it('tolerates leading whitespace and mixed case in the prefix', async () => {
    expect((await parse('  WARN : late')).level).toBe('warn')
  })

  it('does NOT classify on the word "error" appearing inside a success message', async () => {
    // Regression: scanning the whole line for "error" mislabelled this info line.
    const line = await parse('info: Build finished, check the log for any error.')
    expect(line.level).toBe('info')
  })

  it('falls back to [Warning]/[WRN] tags and strips them', async () => {
    expect(await parse('[Warning] disk almost full')).toMatchObject({
      level: 'warn',
      message: 'disk almost full'
    })
    expect((await parse('[WRN] heads up')).level).toBe('warn')
  })

  it('falls back to [Error]/[ERR] tags and strips them', async () => {
    expect(await parse('[Error] boom')).toMatchObject({ level: 'error', message: 'boom' })
    expect((await parse('[ERR] boom')).level).toBe('error')
  })

  it('treats an unrecognised line as info and trims it', async () => {
    expect(await parse('   just some output   ')).toMatchObject({
      level: 'info',
      message: 'just some output'
    })
  })

  it('forces every stderr line to error even when it looks like info', async () => {
    expect((await parse('info: written to stderr', 'err')).level).toBe('error')
  })

  it('stamps a timestamp on every line', async () => {
    expect((await parse('info: hi')).timestamp).toMatch(/^\d{2}:\d{2}:\d{2}$/)
  })
})

// ── stdout/stderr chunk buffering ──

describe('output buffering', () => {
  it('joins a line split across two data chunks', async () => {
    const done = cli.spawnCliAction(WS, 'build', [], onLine)
    const proc = lastProc()
    proc.out('info: half')
    expect(lines).toHaveLength(0) // nothing emitted until the newline arrives
    proc.out(' of a line\ninfo: second\n')
    proc.close(0)
    await done

    expect(messages().slice(0, 2)).toEqual(['info: half of a line', 'info: second'])
  })

  it('flushes a trailing partial line on close', async () => {
    const done = cli.spawnCliAction(WS, 'build', [], onLine)
    const proc = lastProc()
    proc.out('info: no trailing newline')
    proc.err('fail: also unterminated')
    proc.close(0)
    await done

    expect(messages()).toContain('info: no trailing newline')
    const stderrLine = lines.find((l) => l.message === 'fail: also unterminated')
    expect(stderrLine?.level).toBe('error')
  })

  it('skips blank lines', async () => {
    const done = cli.spawnCliAction(WS, 'build', [], onLine)
    const proc = lastProc()
    proc.out('\n   \n\ninfo: real\n')
    proc.close(0)
    await done

    expect(levels()).toHaveLength(1)
  })
})

// ── invocation shape ──

describe('CLI invocation', () => {
  it('runs the dev project through dotnet with the action appended after --', async () => {
    const done = cli.spawnCliAction(WS, 'build', ['my-mod'], onLine)
    lastProc().close(0)
    await done

    const { command, args } = spawnCall()
    expect(command).toBe('dotnet')
    expect(args).toEqual([
      'run', '--project', expect.stringContaining('UMB.CLI'),
      '--no-launch-profile', '--', 'build', 'my-mod'
    ])
  })

  it('runs the bundled exe with bare args when packaged', async () => {
    mocks.app.isPackaged = true
    ;(process as any).resourcesPath = '/app/resources'

    const done = cli.spawnCliAction(WS, 'build', ['my-mod'], onLine)
    lastProc().close(0)
    await done

    const { command, args } = spawnCall()
    expect(command).toContain('UMB.CLI.exe')
    expect(args).toEqual(['build', 'my-mod'])
  })

  it('anchors the CLI to the workspace via cwd and UMB_WORKSPACE', async () => {
    const done = cli.spawnCliAction(WS, 'build', [], onLine)
    lastProc().close(0)
    await done

    const { opts } = spawnCall()
    // Without UMB_WORKSPACE the CLI walks up to its own bundled Resources/ and
    // silently ignores the user's mods.
    expect(opts.cwd).toBe(WS)
    expect(opts.env.UMB_WORKSPACE).toBe(WS)
    expect(opts.stdio).toEqual(['pipe', 'pipe', 'pipe'])
  })
})

// ── exit reporting ──

describe('exit reporting', () => {
  it('reports a clean exit as info and resolves 0', async () => {
    const done = cli.spawnCliAction(WS, 'build', [], onLine)
    lastProc().close(0)

    expect(await done).toBe(0)
    expect(lines.at(-1)).toMatchObject({ level: 'info', message: 'Process exited with code 0' })
  })

  it('reports a non-zero exit as an error and resolves that code', async () => {
    const done = cli.spawnCliAction(WS, 'build', [], onLine)
    lastProc().close(3)

    expect(await done).toBe(3)
    expect(lines.at(-1)).toMatchObject({ level: 'error', message: 'Process exited with code 3' })
  })

  it('reports a signal kill as a warning and resolves -1', async () => {
    const done = cli.spawnCliAction(WS, 'build', [], onLine)
    lastProc().close(null, 'SIGTERM')

    expect(await done).toBe(-1)
    expect(lines.at(-1)).toMatchObject({
      level: 'warn',
      message: 'Process exited before completion (SIGTERM)'
    })
  })

  it('names an unknown signal "terminated"', async () => {
    const done = cli.spawnCliAction(WS, 'build', [], onLine)
    lastProc().close(null, null)

    expect(await done).toBe(-1)
    expect(lines.at(-1)?.message).toBe('Process exited before completion (terminated)')
  })
})

// ── one-shot concurrency guard / cancel ──

describe('one-shot concurrency', () => {
  it('refuses a second action while one is running', async () => {
    const first = cli.spawnCliAction(WS, 'build', [], onLine)
    const second = await cli.spawnCliAction(WS, 'build', [], onLine)

    expect(second).toBe(-1)
    expect(mocks.spawn).toHaveBeenCalledTimes(1)
    expect(lines.at(-1)).toMatchObject({
      level: 'warn',
      message: 'Another CLI action is already running. Cancel it before starting a new one.'
    })

    lastProc().close(0)
    await first
  })

  it('accepts a new action once the previous one has closed', async () => {
    const first = cli.spawnCliAction(WS, 'build', [], onLine)
    lastProc().close(0)
    await first

    const second = cli.spawnCliAction(WS, 'build', [], onLine)
    expect(mocks.spawn).toHaveBeenCalledTimes(2)
    lastProc().close(0)
    expect(await second).toBe(0)
  })

  it('cancelCurrentAction kills the running process and frees the slot', async () => {
    const first = cli.spawnCliAction(WS, 'build', [], onLine)
    const proc = lastProc()

    cli.cancelCurrentAction()
    expect(proc.kill).toHaveBeenCalledTimes(1)

    // The slot is released immediately, so the user can start again at once.
    const second = cli.spawnCliAction(WS, 'build', [], onLine)
    expect(mocks.spawn).toHaveBeenCalledTimes(2)

    proc.close(null, 'SIGTERM')
    lastProc().close(0)
    await Promise.all([first, second])
  })

  it('cancelCurrentAction is a no-op when nothing is running', () => {
    expect(() => cli.cancelCurrentAction()).not.toThrow()
  })
})

// ── daemon routing ──

describe('daemon routing', () => {
  const DAEMON_ACTIONS = [
    'config-volume-analyze',
    'config-volume-save',
    'config-volume-preview',
    'nus3-convert-batch',
    'accept-nus3-batch'
  ]

  it.each(DAEMON_ACTIONS)('routes %s through the persistent daemon', async (action) => {
    const done = cli.spawnCliAction(WS, action, ['in.json'], onLine)
    await flush()

    // The daemon is spawned as `serve`; the action travels over stdin instead.
    expect(spawnCall().args).toContain('serve')
    expect(spawnCall().args).not.toContain(action)

    const written = JSON.parse(lastProc().stdin.write.mock.calls[0][0])
    expect(written).toEqual({ id: 1, action, args: ['in.json'] })

    lastProc().out('__DONE__\t1\t0\n')
    expect(await done).toBe(0)
  })

  it.each(['build', 'order-tracks', 'merge'])(
    'runs %s as a one-shot, not through the daemon',
    async (action) => {
      const done = cli.spawnCliAction(WS, action, [], onLine)
      expect(spawnCall().args).toContain(action)
      expect(spawnCall().args).not.toContain('serve')
      lastProc().close(0)
      await done
    }
  )

  it('reuses one daemon across requests and increments the request id', async () => {
    const first = cli.spawnCliAction(WS, 'config-volume-save', ['a'], onLine)
    await flush()
    const daemon = lastProc()
    daemon.out('__DONE__\t1\t0\n')
    await first

    const second = cli.spawnCliAction(WS, 'config-volume-save', ['b'], onLine)
    await flush()
    daemon.out('__DONE__\t2\t0\n')
    await second

    expect(mocks.spawn).toHaveBeenCalledTimes(1)
    const ids = daemon.stdin.write.mock.calls.map((c) => JSON.parse(c[0]).id)
    expect(ids).toEqual([1, 2])
  })
})

// ── daemon protocol ──

describe('daemon protocol', () => {
  /** Starts a daemon request and returns [promise, daemon]. */
  async function request(action = 'config-volume-save'): Promise<[Promise<number>, FakeProc]> {
    const done = cli.spawnCliAction(WS, action, ['in.json'], onLine)
    await flush()
    return [done, lastProc()]
  }

  it('resolves on the __DONE__ sentinel with the reported exit code', async () => {
    const [done, daemon] = await request()
    daemon.out('__DONE__\t1\t7\n')
    expect(await done).toBe(7)
  })

  it('resolves on a sentinel terminated by CRLF', async () => {
    const [done, daemon] = await request()
    // .NET's WriteLine emits CRLF on Windows; an unstripped '\r' broke the match
    // and the request hung forever.
    daemon.out('__DONE__\t1\t0\r\n')
    expect(await done).toBe(0)
  })

  it('resolves a negative exit code', async () => {
    const [done, daemon] = await request()
    daemon.out('__DONE__\t1\t-1\n')
    expect(await done).toBe(-1)
  })

  it('forwards non-sentinel stdout as parsed log lines', async () => {
    const [done, daemon] = await request()
    daemon.out('info: analyzing\nwarn: slow\n')
    daemon.out('__DONE__\t1\t0\n')
    await done

    expect(lines.map((l) => [l.level, l.message])).toEqual([
      ['info', 'info: analyzing'],
      ['warn', 'warn: slow']
    ])
  })

  it('forwards daemon stderr as errors', async () => {
    const [done, daemon] = await request()
    daemon.err('info: on stderr\n')
    daemon.out('__DONE__\t1\t0\n')
    await done

    expect(lines[0]).toMatchObject({ level: 'error', message: 'info: on stderr' })
  })

  it('does not emit a "Process exited" line for daemon requests', async () => {
    const [done, daemon] = await request()
    daemon.out('__DONE__\t1\t0\n')
    await done

    expect(messages().some((m) => m.startsWith('Process exited'))).toBe(false)
  })

  it('serialises concurrent requests so only one is in flight', async () => {
    const first = cli.spawnCliAction(WS, 'config-volume-save', ['a'], onLine)
    const second = cli.spawnCliAction(WS, 'config-volume-save', ['b'], onLine)
    await flush()
    const daemon = lastProc()

    // One stdin pipe means the second request must wait for the first to finish,
    // otherwise stdout lines could not be attributed to a request.
    expect(daemon.stdin.write).toHaveBeenCalledTimes(1)

    daemon.out('__DONE__\t1\t0\n')
    expect(await first).toBe(0)
    await flush()

    expect(daemon.stdin.write).toHaveBeenCalledTimes(2)
    daemon.out('__DONE__\t2\t0\n')
    expect(await second).toBe(0)
  })

  it('keeps the queue alive after a request whose daemon died', async () => {
    const [first, daemon] = await request()
    daemon.close(1)
    expect(await first).toBe(1)

    // A new daemon is spawned for the next request rather than the chain wedging.
    const second = cli.spawnCliAction(WS, 'config-volume-save', ['b'], onLine)
    await flush()
    expect(mocks.spawn).toHaveBeenCalledTimes(2)
    lastProc().out('__DONE__\t2\t0\n')
    expect(await second).toBe(0)
  })

  it('warns and resolves the pending request when the daemon exits', async () => {
    const [done, daemon] = await request()
    daemon.close(2)

    expect(await done).toBe(2)
    expect(lines.at(-1)).toMatchObject({
      level: 'warn',
      message: 'CLI daemon exited (2); it will restart on the next request.'
    })
  })

  it('describes a signal-killed daemon as terminated', async () => {
    const [done, daemon] = await request()
    daemon.close(null)

    expect(await done).toBe(-1)
    expect(lines.at(-1)?.message).toContain('terminated')
  })

  it('reports a spawn error and resolves -1', async () => {
    const [done, daemon] = await request()
    daemon.emit('error', new Error('ENOENT dotnet'))

    expect(await done).toBe(-1)
    expect(messages()).toContain('CLI daemon error: ENOENT dotnet')
  })

  it('reports an unavailable daemon when spawn yields no stdin', async () => {
    mocks.spawn.mockImplementationOnce(() => {
      const proc = new FakeProc()
      ;(proc as any).stdin = null
      procs.push(proc)
      return proc
    })

    const code = await cli.spawnCliAction(WS, 'config-volume-save', ['a'], onLine)
    expect(code).toBe(-1)
    expect(messages()).toContain('CLI daemon unavailable.')
  })
})

// ── daemon shutdown ──

describe('shutdownDaemon', () => {
  it('sends the shutdown request and kills the process', async () => {
    const done = cli.spawnCliAction(WS, 'config-volume-save', ['a'], onLine)
    await flush()
    const daemon = lastProc()
    daemon.out('__DONE__\t1\t0\n')
    await done

    cli.shutdownDaemon()

    const last = JSON.parse(daemon.stdin.write.mock.calls.at(-1)![0])
    expect(last).toEqual({ id: 0, action: '__shutdown__', args: [] })
    expect(daemon.kill).toHaveBeenCalledTimes(1)
  })

  it('is a no-op when no daemon was started', () => {
    expect(() => cli.shutdownDaemon()).not.toThrow()
    expect(mocks.spawn).not.toHaveBeenCalled()
  })

  it('survives a stdin write that throws', async () => {
    const done = cli.spawnCliAction(WS, 'config-volume-save', ['a'], onLine)
    await flush()
    const daemon = lastProc()
    daemon.out('__DONE__\t1\t0\n')
    await done

    daemon.stdin.write.mockImplementation(() => {
      throw new Error('EPIPE')
    })

    // App quit must not be blocked by an already-dead pipe.
    expect(() => cli.shutdownDaemon()).not.toThrow()
    expect(daemon.kill).toHaveBeenCalledTimes(1)
  })

  it('spawns a fresh daemon for the next request after shutdown', async () => {
    const first = cli.spawnCliAction(WS, 'config-volume-save', ['a'], onLine)
    await flush()
    lastProc().out('__DONE__\t1\t0\n')
    await first

    cli.shutdownDaemon()

    const second = cli.spawnCliAction(WS, 'config-volume-save', ['b'], onLine)
    await flush()
    expect(mocks.spawn).toHaveBeenCalledTimes(2)
    lastProc().out('__DONE__\t2\t0\n')
    expect(await second).toBe(0)
  })
})
