# openHAB Compatibility Fixtures

These are regression fixtures for the openHAB interfaces consumed by this app. They are not general openHAB API documentation.

## Provenance

The `openhab-5.1.4` and `openhab-5.2.0` directories were captured on 2026-07-23 from separate running official Docker images (`openhab/openhab:5.1.4` and `openhab/openhab:5.2.0`). Both instances used the same synthetic `compatibility` sitemap and only synthetic `Compatibility_*` items. They were not copied from documentation or hand-authored API examples.

`sitemaps/button-grid.json` is the exact Buttongrid widget extracted from that version's captured `sitemaps/home.json`; it exists to make the two supported representations easy to test. All other JSON fixtures are the full body from the endpoint named by their path. Each `events/widget-update.sse` file contains frames produced by changing only the disposable `Compatibility_Switch` test item after a real sitemap event subscription.

## Sanitization

- Server URLs were replaced with `https://openhab.test`.
- The source instances were local, unauthenticated, and contained no personal data, tokens, usernames, or passwords.
- Item names, sitemap name, labels, commands, media URLs, and UI text were already synthetic. The image/video proxy routes and their widget IDs are retained because they are part of the response contract.

Do not casually change JSON property names, value kinds, nulls, empty arrays, widget hierarchy, widget/page IDs, commands, `Location` structures, or SSE `data:` framing. Refresh a fixture only by running the documented compatibility capture against a new, isolated server version, then repeat the sanitization and validation checks recorded in the verification results.
