import tailwindcss from "@tailwindcss/vite";

// A static single-page app: Tauri serves the generated files, and in development loads the dev server.
export default defineNuxtConfig({
  compatibilityDate: "2026-10-01",
  ssr: false,
  modules: ["motion-v/nuxt"],
  devtools: { enabled: false },
  css: ["~/assets/css/app.css"],
  devServer: { host: "127.0.0.1", port: 3000 },
  ignore: ["**/src-tauri/**"],
  vite: {
    plugins: [tailwindcss()],
    clearScreen: false,
    envPrefix: ["VITE_", "TAURI_"],
    server: { strictPort: true },
    // Found late at runtime otherwise, which costs a page reload in development.
    optimizeDeps: { include: ["reka-ui", "motion-v", "@tauri-apps/api/core", "@tauri-apps/api/event", "@tauri-apps/api/window"] },
  },
  app: {
    head: {
      htmlAttrs: { lang: "en" },
      title: "MX Studio",
      meta: [
        { name: "viewport", content: "width=device-width, initial-scale=1" },
        { name: "color-scheme", content: "light dark" },
      ],
    },
  },
  typescript: { strict: true },
});
