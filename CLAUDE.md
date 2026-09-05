# CLAUDE.md

## Version control: Jujutsu (`jj`), never git
Colocated repo (`.jj/` + `.git/`). Read-only `git` (`status`/`log`/`diff`) is fine; **all mutations via `jj`**.
- Never run `git commit/add/branch/checkout/reset/rebase/stash/merge/push`.
- Use: `jj st`, `jj diff`, `jj describe -m`, `jj commit -m`, `jj new`, `jj bookmark`, `jj log`, `jj git push/fetch`.
- No staging area (jj auto-snapshots the working copy). Detached-`HEAD`-looking git output is normal jj colocation — don't "fix" it.

## Commits
No `Co-Authored-By` / tooling trailers.

## Project
`Raun`: a Microsoft.Testing.Platform test framework (Given/When/Then DSL, Roslyn source-gen, DAG parallel scheduler, object-flow tracking, self-contained HTML report). Build/test via `dotnet` on `Raun.slnx`.
