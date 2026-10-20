# Animation Patterns

Motion is optional. P2's capture mode disables CSS animation and transition timing so screenshots are deterministic; essential information must therefore be visible without interaction or elapsed time.

## Use sparingly

- Prefer a short opacity or transform transition that clarifies hierarchy or focus. Keep a page understandable when motion is disabled.
- Avoid continuous movement, rapid flashing, scroll-driven sequencing, particle loops, and effects that obscure text or data.
- Do not use animation to reveal required content. The runtime applies the active/visible state when a slide is shown, but authored JavaScript is removed from slide content.
- Do not add a second navigation or timing controller. Use the shared runtime for slide changes.

## Capture behavior

When the URL has `?mode=capture`, `nixer-stage.js` sets `data-nx-mode="capture"`, disables animation and transition durations, hides runtime counters, and selects the page from the hash. Design every page to look complete in this state.

P2 capture does not promise to emulate an animation's final keyframe or wait for arbitrary timers. Prefer static end-state styling; do not make the screenshot depend on a click, hover, or script.

## Accessibility and performance

- Preserve readable contrast and visible focus where authored controls are genuinely needed.
- Keep transitions limited to `opacity` and `transform` when possible.
- Avoid large numbers of animated filters or layout properties.
- Any future user-facing animation should respect reduced-motion preferences; screenshots remain deterministic regardless.