# Project conventions — RonaORAEPubSub

## Versioning

- The internal version is defined in `PubSubApp/PubSubApp.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>` — keep all three in sync).
- **On every version bump, add a matching entry to `README.md` under `## Version History`.**
  - Put the new entry at the top, tagged `✨ Current`, and remove the `✨ Current` tag from the previous entry.
  - Header format: `### vX.Y.Z (MM/DD/YY)` followed by a short bold summary line and bulleted changes.
  - Use the existing entries as the style reference (🔧 for fixes, ✨ for new behavior).
