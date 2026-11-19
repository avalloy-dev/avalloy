---
layout: home

hero:
  name: Avalloy
  text: The layer a desktop app sits on.
  tagline: >-
    App bootstrap, window chrome, a token-driven theme engine, commands and
    keybindings, preferences with hot reload, and an icon pipeline — for
    Avalonia, so the app itself is only the app.
  image:
    src: /marks/avalloy.svg
    alt: Avalloy
  actions:
    - theme: brand
      text: Get started
      link: /guide/getting-started
    - theme: alt
      text: View on GitHub
      link: https://github.com/avalloy-dev/avalloy

features:
  - title: Batteries in, seams out
    details: >-
      Derive from AvalloyApp and you have DI, logging, a single-instance guard,
      relaunch, a splash, and the login-shell PATH fix. Every piece is behind an
      interface you can answer yourself.
  - title: Native chrome, measured around
    details: >-
      AlloyTitleBar never draws caption buttons. It measures around the OS's own —
      traffic lights on macOS, the DWM strip on Windows — so the window looks like
      it belongs on each platform.
  - title: Tokens, not styles
    details: >-
      Every colour, radius, size and face is a resource key. Copy a template, keep
      every key, replace the values: that is a brand theme, and it repaints live.
  - title: Documented at the seam
    details: >-
      The XML doc comments ship in the packages and say what each piece is for and
      what it deliberately does not do. They are the design notes.
---

<div class="av-packages">
  <a class="av-pkg acc-core" href="/core/">
    <div class="glow"></div>
    <div class="av-pkg-head">
      <img src="/marks/core.svg" alt="Avalloy.Desktop" />
      <div>
        <div class="av-pkg-name"><span class="lite">Avalloy.</span>Desktop</div>
        <div class="av-pkg-tag">the app shell</div>
      </div>
    </div>
    <p class="av-pkg-desc">The app class, the window and title bar, the sidebar and tab strip, commands and the palette, preferences, the theme cascade, dialogs, toasts, and the control layer.</p>
    <div class="av-types"><code>AvalloyApp</code><code>ShellWindow</code><code>AlloyTitleBar</code><code>CommandRegistry</code><code>PreferencesService</code><code>ThemeService</code><code>TabStrip</code><code>CliRunner</code></div>
  </a>

  <a class="av-pkg acc-themes" href="/themes/">
    <div class="glow"></div>
    <div class="av-pkg-head">
      <img src="/marks/themes.svg" alt="Native themes, in Avalloy.Desktop" />
      <div>
        <div class="av-pkg-name"><span class="lite">Avalloy.Desktop · </span>Themes</div>
        <div class="av-pkg-tag">the native theme engine</div>
      </div>
    </div>
    <p class="av-pkg-desc">Hand-authored light and dark templates on a token contract, a type ramp, control themes, and a controller that swaps the palette live and follows the OS.</p>
    <div class="av-types"><code>NativeThemeController</code><code>Tokens.axaml</code><code>Template.Light</code><code>Template.Dark</code><code>Type.axaml</code><code>AlloyControls</code></div>
  </a>

  <a class="av-pkg acc-icons" href="/icons/">
    <div class="glow"></div>
    <div class="av-pkg-head">
      <img src="/marks/icons.svg" alt="The icon pipeline" />
      <div>
        <div class="av-pkg-name"><span class="lite">Avalloy · </span>Icons</div>
        <div class="av-pkg-tag">the icon pipeline</div>
      </div>
    </div>
    <p class="av-pkg-desc">One control for every glyph. SVGs bake to XAML geometry at build time, packs swap as a dictionary, and a Lucide baseline ships in the box. The baker is a dotnet tool.</p>
    <div class="av-types"><code>GlyphIcon</code><code>IconResolver</code><code>IconBaker</code><code>IconPickerView</code><code>avalloy-icontool</code></div>
  </a>

</div>
