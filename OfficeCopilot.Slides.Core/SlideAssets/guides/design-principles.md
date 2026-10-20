# Slide Design Principles

## Start with the message

- State the one conclusion the audience should retain, then choose evidence and structure that support it.
- Give each slide one dominant visual idea. Use a comparison, sequence, map, diagram, or evidence-led layout when it communicates better than a pile of cards.
- Use real values and attributed claims. Mark uncertainty; never fabricate metrics or citations to make a slide look complete.

## Make the chosen style specific

- Keep the selected template's actual type system, palette, spacing, and visual vocabulary. Use local bundled fonts and their declared fallbacks; do not add remote font services.
- Avoid generic, interchangeable layouts. Choose a layout because it clarifies the content, not because a repeated card grid is convenient.
- Make color carry meaning and maintain readable contrast. Do not default to a single-hue palette or decorative gradients without a reason tied to the subject.
- Use background texture, shapes, or illustration only when they reinforce the content and do not compete with it.

## Control density and hierarchy

- Low density is for live delivery: fewer ideas, larger type, pauses, and visual evidence.
- High density is for reading: structured evidence and concise annotations, with clear hierarchy and enough space to scan.
- Split content that does not fit. Do not solve overflow by shrinking all text or compressing line spacing.
- Align related elements deliberately; keep consistent gutters and edge insets. Check that labels, footnotes, legends, and data units remain readable.

## Use images and diagrams honestly

- Use images only when they add evidence, context, or a useful focal point. Preserve aspect ratio and crop intentionally.
- Prefer diagrams that show relationships over decorative diagrams with no explanatory role.
- Keep charts legible: label units and series directly where possible, and do not distort scales to exaggerate a conclusion.
- Do not place essential wording only in an image, background, pseudo-element, or animation.

## Review at presentation size

Before asking the user to approve a deck, inspect every slide in `ppt_deck_preview` at its intended 16:9 size. Look for overflow, collisions, weak contrast, inconsistent alignment, ambiguous chart labels, and template chrome left over from sample content. P2 preview is visual review, not an automated validation result.