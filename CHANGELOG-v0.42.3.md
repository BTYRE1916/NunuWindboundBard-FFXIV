# 0.42.3

- Fixed plugin startup when loaded from memory by Dalamud: configuration no longer calls Path.Combine with a directory derived from an empty Assembly.Location.
- Resolve bundled capture paths through Dalamud's AssemblyLocation at runtime; retain custom path overrides.
- Removed the same unsafe reflection lookup from the older configuration migration.
- Added 19 regression checks against the actual memory-loaded plugin assembly and confirmed the old DLL reproduces the reported exception.
- Rebuilt the plugin and self-contained Windows engine; matching repository manifest and release package use version 0.42.3.0.
