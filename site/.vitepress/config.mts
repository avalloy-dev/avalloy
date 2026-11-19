import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'

// The site is keyed by package: /core/, /themes/, /icons/, plus /guide/.
// Each section gets its own sidebar, selected by path prefix below.

const GITHUB = 'https://github.com/avalloy-dev/avalloy'

export default withMermaid(defineConfig({
  title: 'Avalloy',
  description:
    'An application platform for Avalonia desktop apps: app bootstrap, window chrome, a token-driven ' +
    'theme engine, commands and keybindings, preferences with hot reload, and an icon pipeline. ' +
    'MIT licensed.',
  lang: 'en-US',
  cleanUrls: true,
  lastUpdated: true,
  appearance: 'dark',

  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/favicon.svg' }],
    ['meta', { name: 'theme-color', content: '#0F1013' }],
    ['link', { rel: 'preconnect', href: 'https://fonts.googleapis.com' }],
    ['link', { rel: 'preconnect', href: 'https://fonts.gstatic.com', crossorigin: '' }],
    ['link', {
      rel: 'stylesheet',
      href: 'https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&family=JetBrains+Mono:wght@400;500;600;700&display=swap',
    }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:title', content: 'Avalloy — the layer a desktop app sits on' }],
    ['meta', { property: 'og:url', content: 'https://avalloy.dev' }],
    ['meta', { name: 'twitter:card', content: 'summary' }],
  ],

  themeConfig: {
    logo: '/marks/avalloy.svg',
    siteTitle: 'Avalloy',

    nav: [
      { text: 'Guide', link: '/guide/getting-started', activeMatch: '^/guide/' },
      { text: 'Core', link: '/core/', activeMatch: '^/core/' },
      { text: 'Themes', link: '/themes/', activeMatch: '^/themes/' },
      { text: 'Icons', link: '/icons/', activeMatch: '^/icons/' },
      { text: 'NuGet', link: 'https://www.nuget.org/packages?q=Avalloy' },
    ],

    sidebar: {
      '/guide/': [
        {
          text: 'Guide',
          items: [
            { text: 'Getting started', link: '/guide/getting-started' },
            { text: 'The starter app', link: '/guide/starter' },
            { text: 'Building the repo', link: '/guide/building' },
            { text: 'Migrating to 0.23.0', link: '/guide/migrating' },
          ],
        },
      ],
      '/core/': [
        {
          text: 'Avalloy.Base and Avalloy.Desktop — the app shell',
          items: [
            { text: 'Overview', link: '/core/' },
            { text: 'The app', link: '/core/app' },
            { text: 'Windows and the title bar', link: '/core/window' },
            { text: 'Commands and keybindings', link: '/core/commands' },
            { text: 'Preferences', link: '/core/preferences' },
            { text: 'Theming seams', link: '/core/theming' },
            { text: 'Controls', link: '/core/controls' },
            { text: 'Services', link: '/core/services' },
            { text: 'CliRunner', link: '/core/cli' },
          ],
        },
      ],
      '/themes/': [
        {
          text: 'Native themes — in Avalloy.Desktop',
          items: [
            { text: 'Overview', link: '/themes/' },
            { text: 'The token contract', link: '/themes/tokens' },
          ],
        },
      ],
      '/icons/': [
        {
          text: 'Icons — the icon pipeline',
          items: [
            { text: 'Overview', link: '/icons/' },
            { text: 'Baking a pack', link: '/icons/baking' },
          ],
        },
      ],
    },

    socialLinks: [{ icon: 'github', link: GITHUB }],

    editLink: {
      pattern: `${GITHUB}/edit/main/site/:path`,
      text: 'Edit this page on GitHub',
    },

    search: { provider: 'local' },

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Avalonia 12 · .NET 10 · macOS, Windows, Linux.',
    },
  },

  sitemap: { hostname: 'https://avalloy.dev' },

  // mermaid is ESM but pulls a CJS dependency; without pre-bundling it the dev
  // server serves that raw and every page dies on a missing default export.
  vite: {
    optimizeDeps: { include: ['mermaid'] },
    ssr: { noExternal: ['mermaid'] },
  },
}))
