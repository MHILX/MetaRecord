import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const logLevel = resolveLogLevel(env.VITE_LOG_LEVEL);

  return {
    logLevel,
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        '/api': {
          target: env.VITE_API_PROXY_TARGET || 'http://localhost:5000',
          changeOrigin: true
        }
      }
    }
  };
});

function resolveLogLevel(value) {
  const normalized = String(value ?? '').trim().toLowerCase();
  if (normalized === 'info' || normalized === 'warn' || normalized === 'error' || normalized === 'silent') {
    return normalized;
  }

  return 'warn';
}
