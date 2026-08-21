import tailwindcss from '@tailwindcss/vite';

// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
    compatibilityDate: '2025-07-15',
    ssr: false,
    devtools: { enabled: false },
    css: ['~/assets/css/main.css'],
    modules: ['@nuxt/eslint', '@nuxt/ui', 'nuxt-open-fetch', '@nuxtjs/mdc'],
    devServer: { host: '127.0.0.1' },
    openFetch: {
        clients: {
            api: {
                baseURL: '/',
                schema: new URL('./openapi.json', import.meta.url).href,
            },
        },
    },
    vite: { plugins: [tailwindcss()] },
    nitro: {
        preset: 'static',
        prerender: { failOnError: false, crawlLinks: false, routes: ['/'] },
    },
    colorMode: { preference: 'dark', fallback: 'dark' },
    app: {
        head: {
            title: 'Mangette',
            htmlAttrs: { lang: 'en' },
            link: [{ rel: 'icon', type: 'image/svg+xml', href: '/favicon.svg' }],
        },
    },
    spaLoadingTemplate: false,
});
