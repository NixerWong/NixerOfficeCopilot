# Template safe-area source and probe inventory

- Run date: 2026-10-17
- Scope: 34 built-in HTML templates, each rendered with the same four harness probes (cover, body, glyphs, safe-area); 12 Markdown-only presets inspected.
- The layout counts below describe those shared probes under each template wrapper, not every slide in each template's original gallery.
- Source inset values are declarations found in `template.html`; they are not computed text/content boundaries.

## Findings

- 18 templates expose a shared `.slide` padding declaration or `--pad-x`/`--pad-y` variables. In every case, the shared outer inset alone is below the 128/96 design-pixel target on at least one axis. Some inner content wrappers may add further inset; they must be measured before classification.
- 16 templates use per-slide/custom rules or have no shared inset rule detectable by the current source scan. Their content boundaries cannot be inferred from `.slide` padding.
- The fixture probe reports `layout.unsafe`, `layout.tight`, and `layout.chrome_unsafe` counts. The safe-area page intentionally puts text on/over boundaries; those findings are expected and are not template-gallery failure counts.
- The 12 presets are descriptions only and have no fixed HTML or numeric inset to inspect.
- **This is a first-pass inventory, not the final L1 selector map.** No `content`, `background`, or `chrome` selectors are written to `styles.json`: the existing probe and source scan do not establish reliable per-template selectors. The original gallery layouts need selector-level review before adding runtime policy.

## Built-in templates

| Template | Shared source inset | Cover | Body | Glyphs | Safe-area | Total |
|---|---|---:|---:|---:|---:|---:|
| 8-bit-orbit | `.slide` padding `3vh 4vw` | 0 | 0 | 0 | 2 | 2 |
| biennale-yellow | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| block-frame | `.slide` padding `60px / 40px` | 2 | 4 | 8 | 1 | 15 |
| blue-professional | `.slide` padding `2.5vh 3vw` | 2 | 4 | 8 | 2 | 16 |
| bold-poster | `.slide` padding `48px 56px / 32px 24px` | 0 | 0 | 0 | 1 | 1 |
| broadside | `--pad-x 5.5vw; --pad-y 5.5vh` | 2 | 4 | 8 | 1 | 15 |
| capsule | `.slide` padding `3rem 4rem / 2rem` | 2 | 4 | 8 | 1 | 15 |
| cartesian | `.slide` padding `4vh 4vw` | 2 | 4 | 8 | 2 | 16 |
| cobalt-grid | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| coral | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| creative-mode | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| daisy-days | `.slide` padding `40px 60px / 24px 20px` | 0 | 0 | 0 | 2 | 2 |
| editorial-forest | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| editorial-tri-tone | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| emerald-editorial | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| grove | `--pad-x 8vw; --pad-y 6.5vh` | 2 | 4 | 7 | 1 | 14 |
| long-table | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| mat | `--pad-x 5.5vw; --pad-y 5.5vh` | 2 | 4 | 8 | 1 | 15 |
| monochrome | `--pad-x 8vw; --pad-y 6vh` | 2 | 4 | 7 | 1 | 14 |
| neo-grid-bold | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| peoples-platform | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| pin-and-paper | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| pink-script | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| playful | `.slide` padding `3rem 4rem / 2rem` | 2 | 4 | 8 | 1 | 15 |
| raw-grid | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| retro-windows | `.slide` padding `24px 32px 44px 32px / 15px 20px 40px 20px` | 0 | 0 | 0 | 1 | 1 |
| retro-zine | `.slide` padding `60px / 32px` | 0 | 0 | 0 | 1 | 1 |
| sakura-chroma | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| scatterbrain | `.slide` padding `3rem` | 0 | 2 | 2 | 2 | 6 |
| signal | `--pad-x 7.5vw; --pad-y 5.5vh` | 2 | 4 | 7 | 1 | 14 |
| soft-editorial | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| stencil-tablet | Custom/per-slide | 2 | 4 | 8 | 1 | 15 |
| studio | `--pad-x 5vw; --pad-y 5vh` | 2 | 4 | 8 | 1 | 15 |
| vellum | `--pad-x 6vw; --pad-y 6vh` | 2 | 4 | 8 | 1 | 15 |

## Presets

All 12 presets are marked **N/A** for fixed inset: generated HTML determines their eventual layout.
