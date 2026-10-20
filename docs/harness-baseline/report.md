# PPT Harness baseline

- Generated (UTC): 2026-10-16T05:39:17.3885519Z
- Templates measured: 34
- Presets inspected: 12
- Probe pages per template: cover, body, glyphs, safe-area
- The template probe uses the same four golden pages for each style; `text.margin` is counted by the current DOM validator at an 80-design-pixel threshold.

## H2 rendering run

Six representative styles were rendered through Web capture, validation, PowerPoint build/export, and text-shape readback. The run covered 48 slides and 48 PDF pages, with 486 PowerPoint text blocks read back.

| Style | Minimum Web/PPT SSIM |
|---|---:|
| daisy-days | 0.9381 |
| vellum | 0.9541 |
| terminal-green | 0.9444 |
| dark-botanical | 0.9444 |
| studio | 0.9579 |
| bold-signal | 0.9444 |

`text-readback.csv` showed all six cover titles using `Smiley Sans Oblique` with `Italic=false`. This is a font-face mapping observation, not proof that WinForms and PowerPoint are visually identical; confirm again after F3 and in the three actual preview/build surfaces.

The run reported 55 `text.margin`, 0 `text.outside`, 1 `text.overlap`, 50 `font.fallback`, 54 `ppt.font_substituted`, and 9 `ppt.text_overflow` issues. Font/glyph probes deliberately include unsupported or invalid cases, so these totals are diagnostic inputs, not release thresholds.

## Inventory interpretation

- The 34 template rows below were measured with identical content fixtures, not with every slide in each template's original gallery. They measure how each template wrapper styles the golden test content.
- The 323 `text.margin` findings break down as cover 51, body 101, glyphs 171, safe-area 0. `text.outside` was 0. Twenty-eight templates had at least one finding; six had none.
- The source inset column lists shared `.slide` padding declarations and `--pad-x/--pad-y` variables found in `template.html`. Eighteen templates exposed such a shared declaration; the other sixteen use custom/per-slide rules or had no single shared rule detectable by this scan. A source declaration is not a computed content-boundary measurement.
- Presets are Markdown descriptions without fixed slide HTML or numeric content insets. Their generated HTML determines the margin; recording a synthetic count for the preset description itself would be misleading. Therefore all twelve are marked N/A, not zero.

## Selected screenshots

- [daisy-days panel preview](./daisy-days-panel-preview.png), [PowerPoint export](./daisy-days-powerpoint.png), and [difference heatmap](./daisy-days-difference.png)
- Template probe examples: [zero margin hits](./template-low-margin.png), [typical hits](./template-typical-margin.png), and [highest hits](./template-high-margin.png)

| Template | Source inset declarations | Cover | Body | Glyphs | Safe-area | Total margin | Outside |
|---|---|---:|---:|---:|---:|---:|---:|
| 8-bit-orbit | .slide padding 3vh 4vw | 0 | 0 | 0 | 0 | 0 | 0 |
| biennale-yellow | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| block-frame | .slide padding 60px / 40px | 2 | 4 | 7 | 0 | 13 | 0 |
| blue-professional | .slide padding 2.5vh 3vw | 2 | 4 | 7 | 0 | 13 | 0 |
| bold-poster | .slide padding 48px 56px / 32px 24px | 0 | 0 | 0 | 0 | 0 | 0 |
| broadside | --pad-x 5.5vw; --pad-y 5.5vh; .slide padding var(--pad-y) var(--pad-x) | 2 | 3 | 2 | 0 | 7 | 0 |
| capsule | .slide padding 3rem 4rem / 2rem | 2 | 4 | 7 | 0 | 13 | 0 |
| cartesian | .slide padding 4vh 4vw | 2 | 4 | 7 | 0 | 13 | 0 |
| cobalt-grid | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| coral | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| creative-mode | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| daisy-days | .slide padding 40px 60px / 24px 20px | 0 | 0 | 0 | 0 | 0 | 0 |
| editorial-forest | Custom/per-slide; no shared inset rule detected | 0 | 0 | 0 | 0 | 0 | 0 |
| editorial-tri-tone | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| emerald-editorial | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| grove | --pad-x 8vw; --pad-y 6.5vh; .slide padding var(--pad-y) var(--pad-x) | 1 | 2 | 2 | 0 | 5 | 0 |
| long-table | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| mat | --pad-x 5.5vw; --pad-y 5.5vh; .slide padding var(--pad-y) var(--pad-x) | 2 | 3 | 2 | 0 | 7 | 0 |
| monochrome | --pad-x 8vw; --pad-y 6vh | 1 | 2 | 2 | 0 | 5 | 0 |
| neo-grid-bold | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| peoples-platform | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| pin-and-paper | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| pink-script | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| playful | .slide padding 3rem 4rem / 2rem | 2 | 4 | 7 | 0 | 13 | 0 |
| raw-grid | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| retro-windows | .slide padding 24px 32px 44px 32px / 15px 20px 40px 20px | 0 | 0 | 0 | 0 | 0 | 0 |
| retro-zine | .slide padding 60px / 32px | 0 | 0 | 0 | 0 | 0 | 0 |
| sakura-chroma | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| scatterbrain | .slide padding 3rem | 0 | 2 | 2 | 0 | 4 | 0 |
| signal | --pad-x 7.5vw; --pad-y 5.5vh; .slide padding var(--pad-y) var(--pad-x) | 2 | 3 | 2 | 0 | 7 | 0 |
| soft-editorial | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| stencil-tablet | Custom/per-slide; no shared inset rule detected | 2 | 4 | 8 | 0 | 14 | 0 |
| studio | --pad-x 5vw; --pad-y 5vh; .slide padding var(--pad-y) var(--pad-x) | 2 | 4 | 2 | 0 | 8 | 0 |
| vellum | --pad-x 6vw; --pad-y 6vh; .slide padding var(--pad-y) var(--pad-x) | 1 | 2 | 2 | 0 | 5 | 0 |

## Description-only presets

Preset Markdown has no fixed slide HTML or numeric content inset; layout is generated per deck. A margin hit count for a preset without an actual generated HTML deck would be misleading.

| Preset | Measured fixed inset |
|---|---|
| bold-signal | N/A — generated HTML defines the inset |
| creative-voltage | N/A — generated HTML defines the inset |
| dark-botanical | N/A — generated HTML defines the inset |
| electric-studio | N/A — generated HTML defines the inset |
| neon-cyber | N/A — generated HTML defines the inset |
| notebook-tabs | N/A — generated HTML defines the inset |
| paper-and-ink | N/A — generated HTML defines the inset |
| pastel-geometry | N/A — generated HTML defines the inset |
| split-pastel | N/A — generated HTML defines the inset |
| swiss-modern | N/A — generated HTML defines the inset |
| terminal-green | N/A — generated HTML defines the inset |
| vintage-editorial | N/A — generated HTML defines the inset |

## Margin issue details
### biennale-yellow
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### block-frame
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p`: Private use: 
### blue-professional
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p`: Private use: 
### broadside
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Private use: 
### capsule
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p`: Private use: 
### cartesian
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p`: Private use: 
### cobalt-grid
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### coral
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### creative-mode
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### editorial-tri-tone
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### emerald-editorial
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### grove
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Private use: 
### long-table
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### mat
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Private use: 
### monochrome
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Private use: 
### neo-grid-bold
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### peoples-platform
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### pin-and-paper
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### pink-script
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### playful
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p`: Private use: 
### raw-grid
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### sakura-chroma
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### scatterbrain
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Private use: 
### signal
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Private use: 
### soft-editorial
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### stencil-tablet
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Basic: 😀 ☀️ 🌱 ⭐ ✓ ✔ ➜ •
- Slide 3 `p`: Recent: 🫠 🩷 🫶
- Slide 3 `p`: Sequence: 👨‍👩‍👧‍👦 👍🏽 🏳️‍🌈
- Slide 3 `p`: Variation selectors: text︎ emoji️
- Slide 3 `p`: Inline span: visible 😃 emoji
- Slide 3 `p.harness-pseudo`: Pseudo-element sample
- Slide 3 `p`: Private use: 
### studio
- Slide 1 `h1.harness-cover__title`: NIXER_HARNESS_COVER 字体与风格一致性
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `h2`: NIXER_HARNESS_BODY
- Slide 2 `p`: Browsers shape text; PowerPoint lays it out again. Compare the output across all rendering stages.
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Private use: 
### vellum
- Slide 1 `p`: Hamburgefonstiv 0123 汉字永和
- Slide 2 `p.harness-body__eyebrow`: PPT HARNESS · BODY
- Slide 2 `p`: 中英文混排：字体、布局与回退。
- Slide 3 `h1`: Glyph coverage sample
- Slide 3 `p`: Private use: 
