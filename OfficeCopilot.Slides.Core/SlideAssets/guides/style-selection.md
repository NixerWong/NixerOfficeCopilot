# Style Selection

Use the style catalog as the source of truth. Candidate metadata includes `mood`, `tone`, `occasion`, `formality`, `density`, `scheme`, `bestFor`, and `avoidFor`.

## Shortlist

- Match the user's intended feeling first: compare mood, tone, and `bestFor`; use occasion as a clue, not a rigid industry mapping.
- Respect explicit requests such as light/dark, formal/playful, or high/low density. Treat `avoidFor` as a warning to explain, not an automatic veto.
- Offer three meaningfully different directions by default: one dependable/readable option, one more expressive option, and one flexible alternative. Change the mix when the brief or user preference calls for it.
- Check formality and density against the audience and delivery setting. A mismatch is a reason to surface a tradeoff, not to silently override the user's preference.

## Preview and selection

1. Read short style previews before full design documents. Read the complete design system only for shortlisted styles.
2. Use `ppt_style_preview` to render 1-4 authentic cover slides at once. Each cover should use the user's real title, subject, and available evidence; do not put the style name or option label on the slide.
3. Pass the returned `deckId` to `ppt_deck_create` to continue in the same workspace.
4. Present the returned `previewImageId` references in single-select `ask_user_question` options. Copy each `deckId/candidateName/imageVersion` reference exactly; the panel resolves it to the preview image URL. Include concise option labels and useful distinctions; the images are the primary comparison.
5. Once selected, carry that style consistently through every page. Do not combine templates unless the user explicitly asks for a mixed direction.

The default is three choices, but the tool accepts one to four candidates. Do not force a candidate that does not fit merely to satisfy a fixed mix.