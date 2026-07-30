import { resolve } from 'path'
import { defineConfig } from 'vitest/config'
import { svelte } from '@sveltejs/vite-plugin-svelte'

export default defineConfig({
  // Compiles the rune stores (*.svelte.ts) under vitest.
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
