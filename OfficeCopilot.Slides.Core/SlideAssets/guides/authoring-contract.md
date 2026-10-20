# HTML Authoring Contract

This contract applies to HTML sent to `ppt_deck_put_slides` and `ppt_style_preview`. The tools store one sanitized slide at a time; `ppt_deck_validate` checks the assembled deck and `ppt_deck_build` converts it to native PowerPoint slides.

## Slide shape

- Send one `<section class="slide">...</section>` per `slides[].html` entry. Add `data-layout` with a short layout name when useful.
- Design every slide on a fixed 1920 x 1080 CSS-pixel canvas. Use fixed pixel measurements for content inside the slide; do not reflow the slide at mobile breakpoints or size slide content with viewport-relative units.
- Do not include a document shell, navigation, page counter, or custom slide controller. The assembler supplies the template/shell and `runtime/nixer-stage.js` handles navigation.
- The sanitizer removes active/current visibility classes. Do not mark a page active in authored HTML.

## Safe markup and styles

- Use semantic, static HTML: headings, paragraphs, lists, tables, SVG, and decorative elements. Keep visible copy in text elements rather than pseudo-elements or image text.
- Do not emit `<script>`, event-handler attributes, forms/controls, frames, embedded documents, or remote resources. Such content is removed and may leave the slide incomplete.
- Put shared presentation CSS in the optional `css` argument to `ppt_deck_create` or `ppt_deck_put_slides`; page-specific rules may use a `<style>` element in the slide fragment. The `css` argument replaces the whole `theme.css`.
- When the deck is created with `fromPreview`, the chosen cover's CSS is locked into page 1 as `<style data-nx-cover>` and `theme.css` starts empty. Later `css` arguments do not touch the cover; do not copy cover CSS into `theme.css`. To change the cover, overwrite page 1 and keep its `<style data-nx-cover>`.
- Use workspace-relative image paths such as `assets/chart.png`. Do not use absolute paths, protocol-relative URLs, backslashes, or `..` path segments. The image asset must already exist in the deck workspace; an image-import tool is not part of P2.
- CSS `@import` is removed. Unsafe `url()` values are neutralized, script-like CSS constructs are neutralized, and `position: fixed` is changed to `absolute`. Do not rely on these rewrites as a substitute for authoring safe CSS.
- Keep shared template assets unchanged. The assembler resolves trusted template assets under the local `nixer-deck.local` origin; do not add external URLs or script dependencies.

## Class naming

- `theme.css` is injected for the entire deck, and CSS selectors in slide-level `<style>` elements are also document-wide. Plan class names across all slides; do not assume a slide's CSS is scoped to that slide.
- Template CSS is isolated: only rules that apply to the outer stage, `html`/`body`, variables, fonts, and `.slide` itself are kept. Rules for the template's sample content (cards, pills, kickers, headings inside sample slides) are not injected, so implement the look described by the design doc in your own CSS. `ppt_style_get` lists the template structure classes that remain active; do not use those class names in authored HTML.
- Use `cover-*` only for cover decoration, `theme-*` for components intentionally shared across slides, and `sNN-*` for slide-specific classes (for example, `s03-day`). Reserve `nx-*` and `.slide` for runtime and structural classes.
- Do not reuse an unprefixed class declared by `theme.css` on a content slide. If the style is intentionally shared, rename the CSS selector and its HTML usage to `theme-*`; otherwise give it a `cover-*` or slide-specific `sNN-*` name.
- Before writing slide HTML, map the classes already declared by `theme.css` to these namespaces. Keep the cover's HTML and CSS selectors in sync when renaming classes. `ppt_deck_validate` reports class namespace conflicts as errors.

## Layout and content

- Use the selected style's type scale, palette, spacing rhythm, grid, and decorative vocabulary. Make the subject and evidence more prominent than decoration.
- Prefer one clear point per slide. For low density, aim for one idea and 1-3 short supporting points. For high density, use structured comparisons, diagrams, or concise tables; split the slide before shrinking type until it is hard to read.
- Design for no scrolling, clipped text, overlapping panels, or hidden essential content. Keep content in `.nx-content` or `[data-nx-layer="content"]` and use the 128px horizontal / 96px vertical target safe area (minimum 112px / 80px). Use `.nx-chrome` or `[data-nx-layer="chrome"]` for page numbers and logos; these use the 64px / 48px action area. Mark bleedable decoration with `.nx-bg` or `[data-nx-layer="bg"]`. Unmarked elements are treated as content and reported as `layout.unlayered`.
- The runtime makes `.nx-chrome` a full-slide overlay (`left/right:64px; top/bottom:48px`). Either keep it as an overlay and position its children (page number, logo) absolutely inside it, or restyle it as a bar by setting both edges: footer `bottom:40px;top:auto`, header `top:40px;bottom:auto`. Setting only one edge leaves the other at 48px, stretches the box over the whole slide, and centred flex content lands in the middle of the page (`layout.chrome_stretched`). The sanitizer adds the missing `auto` edge automatically.
- Do not use a reduced inset merely to fit the design. An absolute-positioned legacy layout may use exactly 80px / 80px only when its slide has `data-nx-safe-exception="absolute-80"`; the validator reports invalid or undersized overrides. The PowerPoint builder reserves its 3% body-text widening inside the effective safe area.
- Use meaningful `alt` text for informative images. Treat decorative SVG and shapes as decoration, not as a place to hide information.
- Do not put workflow labels such as "preview", "option A", template slugs, file names, or generation notes on the slide.
- The first style search performs a one-time scan of registered system fonts and caches their family names and cmap coverage in `%LOCALAPPDATA%\\NixerOfficeCopilot\\fonts\\system-font-catalog.json`. The cache is reused until the registered font-file list, sizes, or modification times change; then the system is rescanned.
- Use the Chinese-capable system font list returned by `ppt_style_search`; the list is based on actual common Chinese glyph coverage, not a fixed five-family allowlist. The bundled Chinese families remain available if the system scan finds none: `Noto Sans SC`, `Noto Serif SC`, `LXGW WenKai`, `Smiley Sans`, and `ZCOOL XiaoWei`.
- Apply one simple rule: if a text block contains any Chinese, use a Chinese-capable family for the entire block. Do not split mixed Chinese/English copy, and do not include a Latin-only family anywhere in that block's `font-family` stack. `Noto Color Emoji` is reserved for automatic emoji fallback and must not be selected as a text family.
- Prefer weights supported by the selected family; `font.weight_unavailable` is a warning, not a blocking error. Do not request synthetic italic for bundled families whose italic policy is `none` or `builtin`.
- Do not set an emoji-specific `font-family`; the local emoji fallback is supplied automatically.
- Use standard fully-qualified emoji sequences from the bundled Noto Color Emoji coverage. If `glyph.missing` reports an emoji, replace it with a semantically similar covered standard emoji; do not choose a replacement by nearest code point. If no safe alternative exists, rewrite the text without that emoji rather than silently substituting it.
- Emojis are captured as individual transparent images in native builds; keep them decorative and limited to three per slide. Representative ZWJ sequences, skin tones, flags, and variation selectors have passed Office 2019 harness verification.
- Use `<i class="nx-icon" data-icon="leaf"></i>` with a lowercase Lucide icon name and no `lucide-` prefix (for example `leaf`, `bug`, `mountain`, `map-pin`, `calendar`, `chart-column`); about 290 common icons are bundled; a name that is not bundled is replaced with the closest bundled icon (or `sparkle`) and reported in the write result notes, so check those notes. Icons become images that sit above the text in native builds.
- Do not use text symbols such as ✿ ✦ ★ ❀ as decoration; they trigger font fallback and become editable text. Use `nx-icon` or a shape instead.
- Size text in `px` for the 1920×1080 stage. Do not use `vw`, `vh`, or `clamp()` for font sizes.

## Runtime and preview

- The assembler replaces template sample slides and supplies the common runtime. Do not add navigation JavaScript, `postMessage`, custom keyboard handlers, or a second stage scaler.
- In normal preview, use the arrow keys, Page Up/Down, Home/End, wheel, or click to navigate. The runtime reads and updates the one-based slide number in the URL hash.
- `?mode=capture` disables animation/transition timing, suppresses navigation UI, and renders the requested hash page for a PNG capture. Do not make essential content depend on JavaScript timers or interaction.
- Preview with `ppt_deck_preview`; inspect every slide at presentation size and revise the source HTML through `ppt_deck_put_slides`.

## Speaker notes

- Put presenter notes in `<aside class="notes">...</aside>` inside the slide section. Notes are hidden in preview and capture, and `ppt_deck_build` writes their plain text into the PowerPoint speaker notes of that slide.

## Native build

- `ppt_deck_validate` is an optional standalone DOM/PDF check (overflow, overlap, safe area, fonts, pagination). The generation/build flow does not require it; users may request it separately.
- `ppt_deck_build` converts each slide to a native PowerPoint slide: background screenshot, native text boxes, images, native tables, native charts, and shapes. In `editable` mode, solid background boxes, uniform or per-side solid borders, and inline highlights (e.g. `<span>` or `<b>` with a background) become native rectangles automatically; explicit `data-nx-shape="rect|rounded-rect|line"` elements are also converted. Gradients, shadows, border-radius, transforms, opacity below 1, dashed/dotted borders, and `::before`/`::after` pseudo-elements stay in the background screenshot. Short multi-line text (titles, numbers) keeps the browser's line breaks; body text wraps and gets 3% extra width.
- For color blocks, pills, cards, and rules that should stay editable in PowerPoint, add `data-nx-shape="rect"`, `"rounded-rect"`, or `"line"` to the element that carries the background/border. Unmarked rounded or decorated boxes are flattened into the background image, so text placed on them no longer moves with them.
- A `<table>` with at most 20 rows x 12 columns and no `rowspan`/`colspan` becomes a native PowerPoint table. Larger or merged tables stay in the background screenshot with their cell text as separate text boxes; split them across slides instead.
- Declare charts with a `data-nx-chart` attribute holding JSON (HTML-escape the quotes): `<canvas data-nx-chart='{"type":"bar","title":"Revenue","labels":["Q1","Q2"],"series":[{"name":"2025","values":[10,20],"color":"#2563eb"}]}'></canvas>`. `type` is `bar`, `line`, `pie`, or `doughnut`; 1-24 labels; 1-8 series (exactly 1 for pie/doughnut); every series has one finite number per label; `color` is optional (applied natively for bar/line). The runtime draws it with Chart.js in preview and the build creates an editable native chart from the same data. Invalid JSON is not converted.
- `ppt_deck_build` does not automatically read back or visually validate the PowerPoint output. It reports build-time font installation issues; use separate checks only when requested.
- Built slides are tagged with the workspace id; `ppt_get_active_presentation` returns `deckId` so later edits can reopen the same workspace.

## Current phase boundary

Tables, supported `data-nx-chart` charts, captured text, images, and shapes are converted to native objects within their documented limits. Emoji overlays are independent transparent pictures layered above editable text; hand-drawn SVG/CSS charts and unsupported decorations stay in the screenshot. Image import is not provided. Do not claim anything else was converted.