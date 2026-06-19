# Copilot Instructions

## Project Guidelines
- User prefers straightforward DevExpress implementations following official examples; avoid custom/manual workarounds when a standard DevExpress Grid feature exists.
- The ObracunDb project does NOT load Bootstrap; only DevExpress CSS, site.css, and ObracunDb.styles.css are referenced in App.razor. Bootstrap utility classes like d-inline-flex, align-items-center, gap-2, ms-3, btn, btn-link do NOT work. For inline layout (e.g., single-row filter bars), use `style="display:inline-flex; align-items:center; gap:8px"` as in PregledNalogov.razor, not Bootstrap classes.