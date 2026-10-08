# Engineering metrics design system

## 1. Atmosphere & Identity
A plain engineering notebook. Dates, commit identity, measurement scope and missing evidence are more prominent than decoration. This design applies only to the internal metrics report; it does not define game UI.

## 2. Color
Tokens: `--paper: #fbfbfa`, `--ink: #242424`, `--muted: #555550`, `--rule: #d5d5ce`, `--accent: #175c45`, `--focus: #964b00`. Accent identifies measurements and links. White backgrounds and dark text preserve contrast. No dark mode.

## 3. Typography
System UI body 16px, line height 1.6; H1 28px and H2 20px, bold; metadata 14px. System monospace for commit/runtime/config identifiers. No downloaded fonts. SVG labels use 14 viewBox units on desktop and 28 below 600px so the responsive scale keeps rendered labels readable.

## 4. Spacing & Layout
Base 4px. Tokens `--s2: 8px`, `--s4: 16px`, `--s6: 24px`, `--s8: 32px`. Single column, max 1120px, 16px minimum side padding. Tables scroll within a labelled focusable region. SVG viewBox 720×220 includes plot padding 48, vertical top 20, bottom 172; charts resize with the page.

## 5. Components
Trend section: heading, scope/provenance description, SVG chart, accessible tabular values. Chart labels include dates/commits, unit, zero baseline and upper bound; exact values remain in the table. Groups separate stage, model, runtime, config, measurement scope. Raw-record links are normal keyboard accessible anchors; :hover/:active underline and focus uses 3px outline. No controls masquerading as working buttons.

## 6. Motion & Interaction
No animation. The static report works from file:// with no scripts, network or loading state. Missing balance metrics show “Not measured”; invalid input fails the generator without publishing a report. Empty history shows explicit no-measurement text.

## 7. Depth & Surface
Borders-only: 1px `--rule` for table rows and chart baseline. No shadows, gradients or raster substitutes for data.

Game UI uses the separate [Phase1A screen system](docs/design/05_phase1a-game-design-system.md); the internal engineering report contract above remains unchanged.
