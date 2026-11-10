# Changelog

## 1.0.0 — 2026-11-03

Initial public release.

- Weld joint count (BW, SW; flanges and screwed joints in their own columns), inch-dia, pipe length and inch-meter per pipeline on PDMS / E3D models in Navisworks.
- Totals per line, area, hierarchy and size. Lines with missing or assumed data are marked CHECK, never silently filled in.
- Editable `rules.json` (defaults in `docs/rules.default.json`), Inspect selection, Excel / CSV export.
- Optional import of pipe parents saved by MD Hierarchy Scan.
- No-admin per-user installer and a portable zip for Navisworks Manage and Simulate 2017–2027.
- Core unit tests (no Navisworks required to run them). A sample report is in `docs/DiaInch_sample_demo.xlsx`.
