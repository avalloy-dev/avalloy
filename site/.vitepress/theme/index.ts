import DefaultTheme from 'vitepress/theme'
import type { Theme } from 'vitepress'
import './custom.css'

// Avalloy docs theme: VitePress default theme + the Avalloy palette.
// Tokens, type, and per-package accents live in custom.css.
export default {
  extends: DefaultTheme,
} satisfies Theme
