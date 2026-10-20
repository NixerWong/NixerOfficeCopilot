# Template Adaptation Rules

Templates are trusted local design systems, not files to clone into each deck. `DeckAssembler` loads the selected catalog entry, keeps its relevant structural shell and styles, replaces its sample slides with workspace slides, and adds the shared Nixer runtime.

## Read selectively

1. Use `ppt_style_search` to shortlist styles from metadata.
2. Use `ppt_style_get` with `part="preview"` for shortlisting. After choosing a style, read only that entry's design system and the layout samples that fit the content.
3. Keep candidate preview slides grounded in the user's real topic. Never print style names, slugs, option labels, source-file names, or workflow notes on the slide.

## Preserve the selected system

- Preserve its palette, font families and weights, grid, spacing rhythm, component grammar, and distinctive decoration.
- Replace sample copy, figures, labels, dates, and placeholder imagery with the user's content. Never invent data to fill a visual slot.
- Reuse an existing layout when it fits. If a required layout is missing, design it from the selected system rather than mixing in a second template.
- Keep template-owned images and CSS at their existing relative locations. Reference deck-owned assets only with safe workspace-relative paths.

## What the assembler does

- For a template style, the assembler resolves local assets against the template directory, retains the structural path that contains its slides, removes sample slides and surrounding navigation chrome, then inserts workspace slides.
- The assembler removes template body scripts and the template's `deck-stage.js` reference. A small set of trusted head scripts may be retained; do not add new dependencies or external resources.
- For a preset or a style without a usable template, it supplies the standard 1920 x 1080 stage shell and shared runtime.
- The assembler adds `nixer-stage.js`; it handles navigation for both `<deck-stage>` templates and generic `.slide` sections. Do not copy a template's navigation controls into the authored slide.

## Working loop

1. Create or adopt a workspace with `ppt_deck_create`.
2. Add or replace pages in batches of at most 10 with `ppt_deck_put_slides`.
3. Use `ppt_deck_get_slides` before editing an existing page; preserve its id when replacing it.
4. Call `ppt_deck_preview` and inspect the full deck. Revise with the same style system.

Do not claim automated overflow, PDF, or native-PowerPoint validation in P2. Those are later phases.