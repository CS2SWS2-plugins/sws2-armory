# sws2-armory development

- Build on SwiftlyS2 / .NET 10. Keep code in src/, templates/translations/catalog in resources/, checks in tests/, tooling in tools/.
- Develop and commit on develop. main is the default stable branch. The maintainer opens and merges PRs after server testing; do not automatically open or merge a PR.
- After each develop commit, build a test ZIP. Run the CI format/analyzer, warnings-as-errors build, regression checks and package validation before pushing. Record actual validation and distinguish it from pending CS2 server checks.
- Preserve menu contracts export packaging and the single sws2-armory/ ZIP root. Never ship host SwiftlyS2 DLLs.
- All native entity operations run on the game thread. Delayed operations must resolve current players/entities and check session IDs/full entity handles; do not capture native wrappers across frames.
- Keep per-player data in Core.PluginDataDirectory, not the install directory. Do not overwrite corrupt player data with defaults.
- Preserve third-party licenses and catalog manifest provenance when updating imported data. Do not claim catalog completeness for game content added after the pinned snapshot.
- Keep CI to Code checks and Test package for PRs/develop. Stable releases and automatic version increments occur on main; local packaging does not publish a stable release.
