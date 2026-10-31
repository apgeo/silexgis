# Contributing: building, testing, and the rules of the test gates

This page is for whoever changes SilexGIS: what to run before a change is finished, which of the
repository's runners to use for which tier of test, and the handful of ways a test run reports
success without having tested anything. Each rule below is here because breaking it cost a day of
somebody's machine at least once.

The application is described in the [README](../README.md), its installation in
[INSTALL.md](INSTALL.md) and its handbook in [user-guide/](user-guide/README.md). Client-only
instructions are in [client/README.md](../client/README.md).

## Checkout and toolchain

- Clone with submodules: `git clone --recurse-submodules …`. The survey-format readers live in a
  submodule, and a checkout without it fails at restore with a message about a missing project
  path, not about the missing checkout step.
- The .NET 10 SDK and **Node.js 24** (the client declares it, and hosted CI runs 24).
  Docker is needed for the integration and browser suites, which start their own PostGIS.
- The code must run on **Linux and Windows**: `Path.Combine` and `path.join`, no shell-isms in
  scripts (use Node or `dotnet`), file names safe on a case-sensitive file system, LF line endings,
  UTC in the database. Hosted CI runs the server build, the Domain and Architecture tests and the
  client gate on both systems; do not simplify a path join away because your machine tolerates it.

## The tiers

| Tier | What it is | Takes |
|---|---|---|
| **Fast tier** | `dotnet build server/SilexGis.slnx` (warnings are errors), the Domain and Architecture test projects, in `client/` `npx tsc -b --force`, `npm run lint`, `npm run lint:deps`, `npm test`, and `node --test scripts/*.test.mjs` | minutes |
| **Integration** | the API suite (`server/tests/SilexGis.Api.Tests`): real HTTP against a Testcontainers PostGIS | targeted classes: minutes; the whole suite: tens of minutes to hours |
| **Browser** | the Playwright specs in `client/e2e/`, through `node scripts/e2e.mjs` | targeted: minutes; the whole suite: about 20 minutes |

Hosted CI runs the fast tier on every pull request, and the integration and browser suites in
workflows of their own. After any change to a route or a DTO the generated client is regenerated
(see [client/README.md](../client/README.md)) and the diff must be explainable by your change alone.

## Rules for the integration suite

**Name the project file, never the solution.** `dotnet test` is given
`server/tests/SilexGis.Api.Tests/SilexGis.Api.Tests.csproj`, or one of the other test projects.
The solution also contains the submodule's test projects; testing it once ran six hours without a
verdict where the project form takes under two.

**A run of zero tests is not a verdict.** A filter that matches no class can exit 0 and print
`Total: 0`. So does a run whose tests all failed to reach the database, if the exit code is all
that is read. Read the count: `scripts/gate-lock.mjs` writes a result file whose `verdict` is
`green` only when a runner summary was seen, it reported tests, and none failed, and
`inconclusive` — with the reason spelled out — for anything else. Quote that verdict, not the exit
code.

**One integration run at a time per machine, through the gate lock.**

```bash
node scripts/gate-lock.mjs status
node scripts/gate-lock.mjs run --label "my change" --result result.json -- \
  dotnet test server/tests/SilexGis.Api.Tests/SilexGis.Api.Tests.csproj --no-build --filter "<classes>"
```

Two suites on one machine do not share it; they exhaust file-watcher handles and load, and a suite
that passes alone fails wholesale. The lock is one file (`gate.lock`) in a persistent directory
(`SILEXGIS_GATE_LOCK_DIR` overrides it, and must then be the same for every checkout on the
machine), waiters go in the order they arrived, and a holder that has used no CPU for a quarter of
an hour is taken over. Consequences:

- **Never put `timeout` around `gate-lock.mjs run`.** It bounds the wait in the queue, not the
  work: the run is killed, having executed nothing, and nothing says so.
- Do not wrap a `gate-lock.mjs run` in another lock of your own: a process waiting for the gate
  while holding another lock blocks everything behind it for nothing.
- Build first, then run the tests with `--no-build`. The lock fingerprints the test assemblies
  and voids a run (verdict `void`, exit 75) if another build overwrites them part-way, because a
  verdict assembled from two builds proves nothing.
- If tests fail wholesale at startup with an `inotify` instance-limit message, the machine's
  limit is the cause, not your change; set `DOTNET_hostBuilder__reloadConfigOnChange=false` (two
  underscores) for the run, which stops the test hosts creating file watchers.

**The whole suite is deliberate.** An unfiltered run of the API suite is refused unless `--full` is
given to the lock. Run it in shards, which is how it finishes in under an hour instead of a day:

```bash
node scripts/gate-lock.mjs run --full --label "whole suite" --result result.json -- \
  node scripts/gate-sharded.mjs
```

`gate-sharded.mjs --classes A,B` and `--affected <base>` deal only some classes (exit code 10 means
the change selects the whole suite). `node scripts/gate-affected.mjs --base master --filter`
prints the `--filter` expression for the classes a change answers for; it is an early-warning
tier, never the landing verdict.

**A new `*Tests.cs` class needs an owner.** Add it to its group in `scripts/gate-affected.map.mjs`,
run `node scripts/gate-affected.mjs --rederive`, then `node --test scripts/*.test.mjs`. The script
tests fail when a class has no owner or when the map's derived lists have drifted from the sources.

**DEM tiles.** The API suite has a check against real elevation tiles. Set
`SILEXGIS_TEST_DEM_DIR` to a directory holding them, or to `none` (what hosted CI does); a test run
with the variable unset checks nothing real.

**Two test modes.** `precise` (the default) builds an application for every test; `fast`
(`--mode fast` on the lock or on `gate-sharded.mjs`, or `SILEXGIS_TEST_MODE=fast`) lets the tests
of a class take turns on one application, several times quicker, and proves less. Use fast to
iterate, and report it as `green-fast`. A failure seen only in fast mode is re-run in the ordinary
form before it is believed or dismissed, never written off as "only fast mode". The verdict a
change is merged on is the ordinary form.

## Rules for the browser suite

- Run it through `node scripts/e2e.mjs [spec …] [--project=desktop] [--workers=N]`, never
  `npx playwright test` directly: the runner starts its own database, API and dev server on its own
  ports, takes them down, refuses ports already in use, and writes the report it judges.
- **The verdict is the runner's closing lines** (and its exit code, which is non-zero for any
  failing, interrupted or retried test, any error outside a test, or a failure it cannot explain),
  not the pass count of the first attempt. The report it judges is
  `client/<output directory>/report.json`; where a count and the report disagree, the report is
  right. A failure during which the browser reported the network changing under it is run again on
  its own and the second result is the verdict; every other failure stands.
- **Never add Playwright `retries`.** A test that fails one time in three would be reported as
  passed. The runner's single, narrow re-run is the only one.
- `--mode fast` (desktop only, no 3D scenes) and `--affected <base>` are for iterating; the run's
  first and last lines say what did not run. A spec named on the command line runs whatever the mode.
- Every spec imports `test` from `client/e2e/consoleGuard.ts`, which is what records browser errors.
  A spec importing Playwright's `test` directly is silently unwatched. A new spec also needs its line
  in `scripts/e2e-affected.map.mjs`.
- Software rendering makes the 3D specs slow; run anything with the viewer at `--workers=1`. If the
  default worker count is flaky on a loaded machine, lower it and say so; do not raise a timeout.

## Working on a shared machine

- **Stop a process by the pid its launch printed or by the port it holds**
  (`ss -ltnpH "sport = :<port>"`). Never `pkill -f`, `killall`, or a loop that kills whatever `pgrep`
  matches: the pattern also matches other checkouts' test hosts and the shell running it, and a
  killed suite loses its verdict with no error to explain it.
- Keep bulk output, logs and scratch files out of the repository and out of a RAM-backed `/tmp`.
  A full `/tmp` breaks the shell's own output capture and makes a running suite fail with errors
  that name the login, not the disk.
- **Never start an API without an explicit connection string** (`SILEXGIS__Db__ConnectionString`)
  and port when more than one checkout shares the machine: the default settings migrate the
  database every checkout shares.
- Bind development services to `127.0.0.1`, not `0.0.0.0`.
- A red result taken while the machine was loaded is re-run alone before it is believed, and a
  red that is also red on `master` is reported with that evidence, never loosened away.

## Rules for the change itself

- Tests land with the code: unit tests for domain logic, integration tests for every endpoint
  (happy path, validation, 401/403, visibility, and a protected position wherever one can be
  emitted). Never skip, disable or weaken a test to make a build pass; fix the cause.
- All user-facing strings go through `t()` with English and Romanian added together.
- A change to the database is an ordinary incremental migration. Existing rows are carried across:
  columns added nullable or with a default, a rename done as a rename.
- No new NuGet or npm dependency without a recorded reason; look at what is installed first.
- Conventional commit messages that describe the change itself, one logical change per commit.
