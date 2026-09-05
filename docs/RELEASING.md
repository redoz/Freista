# Releasing Freista

Versions come from git tags via [MinVer](https://github.com/adamralph/minver). There is no version
number in any file. Three packages ship from one tag, in lockstep: `Freista`, `Freista.Mtp` (which
carries the source generator as an analyzer), and `Freista.Aspire`.

## Version scheme

- `v0.x.y` while pre-1.0: anything may change between minors.
- An untagged commit builds as `<next patch>-preview.0.<height>`, e.g. `0.1.1-preview.0.7`. Fine
  for local packing; never published.
- A pre-release is a tag with a suffix: `v0.2.0-beta.1`. MinVer uses it verbatim.

## Checklist

1. `main` is green: `dotnet build Freista.slnx` (0 warnings) and `dotnet test Freista.slnx`.
2. Move the analyzer rules being released from `src/Freista.Generator/AnalyzerReleases.Unshipped.md`
   to `AnalyzerReleases.Shipped.md` under a `## Release X.Y.Z` heading (RS2000 release tracking).
   Commit that first.
3. Check the packages locally and look at the file names — they carry the version MinVer computed
   for the current commit:

   ```bash
   dotnet pack src/Freista/Freista.csproj -c Release -o artifacts
   dotnet pack src/Freista.Mtp/Freista.Mtp.csproj -c Release -o artifacts
   dotnet pack src/Freista.Aspire/Freista.Aspire.csproj -c Release -o artifacts
   ```

   To try them as a consumer would, point a scratch project's `nuget.config` at `artifacts/` (with
   `<clear/>` and nuget.org as the second source) and reference `Freista.Mtp` with version `*-*`.
4. Tag with git — jj imports tags but does not create them:

   ```bash
   git tag -a v0.1.0 -m "Freista 0.1.0"
   git push origin v0.1.0
   jj git fetch
   ```

5. The `Release` workflow builds, tests, packs with `ContinuousIntegrationBuild`, pushes to the
   repository's **GitHub Packages** feed, and creates the GitHub release with the packages attached.
   It authenticates with the workflow's own `GITHUB_TOKEN`; no secret to set up.
6. Verify under the repository's Packages tab that all three packages show the version, the license
   (Apache-2.0), and the README.

## Where the packages live (for now)

- **Feed:** `https://nuget.pkg.github.com/redoz/index.json` (GitHub Packages).
- **Previews:** every push to `main` publishes its `0.x.y-preview.0.N` build via the CI workflow, so
  the current head is always consumable without tagging.
- **Consuming:** GitHub Packages requires authentication even for public packages. A consumer needs
  a `nuget.config` with the feed and a personal access token that has `read:packages`:

  ```xml
  <configuration>
    <packageSources>
      <add key="freista" value="https://nuget.pkg.github.com/redoz/index.json" />
    </packageSources>
    <packageSourceCredentials>
      <freista>
        <add key="Username" value="GITHUB_USERNAME" />
        <add key="ClearTextPassword" value="%GITHUB_PACKAGES_TOKEN%" />
      </freista>
    </packageSourceCredentials>
  </configuration>
  ```

  Then `dotnet add package Freista.Mtp --prerelease`.
- **Moving to nuget.org later:** in both workflows change `--source` to
  `https://api.nuget.org/v3/index.json` and `--api-key` to a `NUGET_API_KEY` secret. Versions and
  tags stay exactly as they are.

## Consumer baseline

The generator is packed under `analyzers/dotnet/roslyn5.3/cs`. A consumer whose compiler is older
than Roslyn 5.3 gets no generator and no diagnostics, silently. Supported baseline today: the .NET 10
SDK. Adding an older baseline means a `Freista.Generator.RoslynNN` variant project (see
`Directory.Build.props`), not a version bump.

## Undoing a bad release

Packages cannot be deleted from nuget.org, only unlisted. Tag the fix as the next patch and unlist
the bad version in the nuget.org package settings.
