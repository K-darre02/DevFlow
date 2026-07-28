import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    // DevFlow.Web (ASP.NET Core) serves this directory as static files —
    // see docs/devflow/05-technical-decisions.md ADR 6 and Program.cs in
    // src/DevFlow.Web. `npm run build` here populates it directly.
    outDir: '../src/DevFlow.Web/wwwroot',
    emptyOutDir: true,
  },
  server: {
    // Local-dev stand-in for the production "linked backend" same-origin
    // proxy (Azure Static Web Apps, see Architecture §2/§6): the browser
    // only ever talks to the Vite dev server, which forwards /api requests
    // to DevFlow.Api server-side — no CORS involved in this path at all.
    proxy: {
      '/api': {
        target: 'http://localhost:5187',
        changeOrigin: true,
      },
    },
  },
})
