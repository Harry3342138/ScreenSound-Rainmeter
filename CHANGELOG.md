# Changelog

## 2.1.0-rainmeter.1 — prerelease

- Keep monitor-to-speaker routing stable when an observed player is minimized or hidden.
- Resolve a player that starts minimized using its restored placement; validate cached handles and remove stale session entries.
- Add opt-in per-monitor Rainmeter AudioLevel synchronization in Settings.
- Back up skin INIs before the first change; offer restoration and an optional 33 ms update limit.
- Preserve skin encodings and unrelated settings; avoid file writes/refreshes when assignments are unchanged.
- Publish distinct Rainmeter Edition shortcuts, window/tray branding, update links, and per-user installation scripts.
- Add deterministic monitor and Rainmeter regression suites, an interactive native check, and bilingual documentation.

Based on upstream ScreenSound 2.1.0 plus the upstream MIT license and repository maintenance changes through `c366d70`.
