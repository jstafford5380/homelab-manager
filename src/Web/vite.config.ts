import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { "@": "/src" } },
  server: { proxy: { "/api": process.env.API_URL || "http://localhost:5080" } },
  build: { outDir: "dist", emptyOutDir: true },
});
