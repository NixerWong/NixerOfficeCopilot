# Style Presets Reference

Presets are descriptive starting points in the catalog, not HTML templates. Read the selected preset's `preset.md`, then author slides under the shared HTML contract. The available bundled font families and fallbacks are listed in `fonts/fonts.json` and `fonts/fonts.css`.

## Design checks

- Give the preset a recognizable visual thesis: an intentional palette, typography hierarchy, spacing rhythm, and layout vocabulary.
- Avoid generic defaults such as a repeated card grid on every page, arbitrary accent colors, and decoration without a content role.
- Do not require Google Fonts, Fontshare, CDNs, or other network services. Use bundled fonts or a deliberate system fallback.
- Avoid layout calculations tied to the browser window. Every slide uses the fixed 1920 x 1080 design surface.
- Use the selected density to decide how much information belongs on a page. Split a crowded slide instead of shrinking it.

## CSS checks

- Keep common tokens in `:root` and use them consistently; scope page-specific rules with a layout class when needed.
- Avoid external `@import` and remote `url()` references; sanitization removes or neutralizes them.
- Do not use `position: fixed`; the sanitizer changes it to `absolute` within the slide.
- To negate a CSS function, use `calc(-1 * min(...))` or `calc(-1 * clamp(...))`; a leading minus before the function is invalid CSS.