# Metrics artifact browser QA

Result: PASS for the checked static-report surface. This is not a gameplay or mobile-performance verdict.

- Artifact: `docs/metrics/index.html`, SHA-256 `0ac8105740bc885bb16b72c0f61c75ff125c6b27a694f36ad15a25971f1c93c7`.
- Actual measurement commit: `aafd7b6f671756d1b6f273876a50f02a996e8983`; source runtime Ubuntu 24.04.5 LTS, X64, .NET 8.0.31. The browser runs locally; the measurements shown came from Linux CI.
- Chromium 153.0.8010.12, file://, at 375×1000, 768×1000 and 1280×1000. Full-page captures are `375.png`, `768.png`, `1280.png`.
- Each viewport retained the full body width without page overflow; tabular overflow stays in its keyboard-focusable scroll region. Labels, headings and metadata were visually inspected. Two real SVG plots and an exact-value table render from the record.
- Keyboard Tab reaches the raw-record anchor; Enter opened the exact JSON record. No browser page errors. `browser.json` records viewport checks and `provenance.json` records artifact/record hashes and navigation verification.
- S0 scaffold timing and policy damage CV are labelled separately from game balance. Balance dispersion is unmeasured (`null`). One actual CI commit is shown, so this capture alone does not demonstrate a multi-commit trend.
- No Lighthouse scores are claimed. No product dependencies were added. The rendering code and design system were unchanged during this capture.
