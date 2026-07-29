import { resolve } from 'path'
import { defineConfig } from 'vitest/config'
import { svelte } from '@sveltejs/vite-plugin-svelte'

export default defineConfig({
  // Needed so the rune-based stores (`*.svelte.ts`) compile under vitest. The plugin
  // only transforms .svelte/.svelte.ts, so the main-process tests are unaffected.
  // The store tests stay on the node environment and stub the handful of browser
  // globals they touch, which avoids pulling jsdom in as a dependency.
  plugins: [svelte({ hot: false })],
  test: {
    environment: 'node',
    include: ['src/main/**/*.test.ts', 'src/renderer/**/*.test.ts'],
    coverage: {
      provider: 'v8',
      include: ['src/main/**/*.ts', 'src/renderer/src/lib/stores/**/*.ts'],
      exclude: [
        'src/main/**/*.test.ts',
        'src/renderer/**/*.test.ts',
        // Electron entry points; covered by the Playwright E2E suite instead.
        'src/main/index.ts',
        'src/main/preload.ts'
      ]
    }
  },
  resolve: {
    alias: {
      $shared: resolve(__dirname, 'src/shared'),
      $lib: resolve(__dirname, 'src/renderer/src/lib')
    }
  }
})
