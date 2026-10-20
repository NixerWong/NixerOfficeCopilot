---
name: beautiful-html-templates
description: Build beautiful HTML decks by picking from 34 curated templates. Use when the user wants to create a slide deck with polished, distinctive design — pick template by mood/occasion, preview 3 candidates, and build the full deck. Also works as a template library for frontend-slides.
---

# Beautiful HTML Templates

A library of 34 curated HTML slide templates. Every template follows the zero-dependency pattern: single HTML file with inline CSS/JS, fixed 1920x1080 stage, ready to run in the browser.

## When to Use

- User wants a polished, design-forward slide deck with a specific mood/tone
- User has content ready and needs the right visual presentation
- Use together with frontend-slides when that skill needs template references
- For quick/rough slides, use frontend-slides alone; for finished/polished decks, use this

## Core Workflow

Follow the sequence in AGENTS.md:

1. Ask about occasion and mood
2. Read index.json — pick 3 candidates matching the brief
3. Build title-slide previews for all 3 candidates with the user's real content
4. Open previews in browser — let the user compare and pick
5. Build the full deck using the chosen template
6. Open final deck in browser

## Key Rules

- Always show 3 previews — never pick a template without user comparison
- Preserve the template's design system — fonts, colors, spacing, decorative vocabulary
- If a needed layout is missing, design it from scratch using the template's visual language
- Fixed 16:9 stage (1920x1080) is non-negotiable
- Zero dependencies — every output is a standalone HTML file

## File Structure

- index.json — all 34 templates with mood/tone/best_for/screenshots
- templates/<name>/ — each template's HTML, CSS, JS, and assets
- runtime/deck-stage.js — shared deck navigation runtime
- screenshots/ — preview images for template selection
