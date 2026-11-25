# Contributing to Nyra

Open PRs against the `dev` branch. For a big change, open an issue first to discuss it. Keep your branch up to date by rebasing on `dev`, not by merging it. Skip comments in code, since names and XML docs on public members are enough.

## Commits and PR titles

Use these prefixes.

- `feat:` for new functionality
- `fix:` for bug fixes and test fixes
- `docs:` for documentation changes
- `refactor:` for reorganizing existing code
- `chore:` for changes that preserve Nyra's functionality, like formatting, CI updates, and adding test coverage

If it's a breaking change, put `!` before the colon.

A commit body is optional. Say why in a short paragraph, or use one bullet per change if the commit does several things.

## Tests

A test should fail when something breaks, not when the code changes. Reflection breaks on harmless renames and sleeps race the clock, so avoid both and wait for the task or event the test needs. `FakeTimeProvider` is handy for advancing time, and `FakeNodeServer` and `FakeBridge` in `FakeNode.cs` stand in for the node and the Discord side.

## Local checks

Test changes that affect playback or server integration on both NodeLink and Lavalink. If you can only test one, leave the other unchecked in the PR.

You'll need the .NET 10 and .NET 11 SDKs and CSharpier.

```sh
csharpier format .
CI=true dotnet build --configuration Release

for framework in net10.0 net11.0; do
  dotnet run --project tests/Nyra.Tests --configuration Release --no-build -f "$framework"
done
```
