# Quality gates and e2e

## Development environment

Use the isolated Ubuntu WSL checkout for this work:
`/home/nwfm/work/NWFM`. The existing Windows checkout is unchanged.

Install Node from `.nvmrc`, .NET 10 SDK 10.0.401 or a later 10.0 feature band
(as selected by `global.json`), Git, and Docker. On Windows, enable the
Ubuntu distribution in Docker Desktop's WSL Integration settings. Run:

```sh
npm ci
npm --prefix frontend ci
npx @e2e-dev/web install chromium --with-deps
dotnet restore backend/NWFM.sln
```

Husky installs through `npm ci`. Commit from a feature branch. It formats staged
files, lints changed code, and runs both complete unit suites while lint-staged
hides unstaged work. Failures restore the original index/worktree state.
Conventional Commit examples: `feat: add login button`, `fix: resolve memory leak`.

Use `npm run test:unit`, `npm run build:frontend`, `npm run build:backend`,
`npm run typecheck`, and `npm run security:dependencies` before review.
Check a PR's changed files with
`npm run quality:check -- --base <base-commit-sha>`.

Formatting and linting cover changed files, not old untouched debt. Generated API
client files, migrations, installed skills, recordings and build output are excluded.
Full tests, builds and dependency/secret scans are always required.

## Local end-to-end tests

Read `.agents/skills/e2e/SKILL.md`. Sign in locally:

```sh
npx e2e login openai
npx e2e models openai
npm run test:e2e -- tests/example.e2e.ts
npm run test:e2e -- tests/workflow-lifecycle.e2e.ts
```

Confirm sign-in to the coding agent before it makes model calls. Local agent
steps use `chatgpt('gpt-6-luna')`; no OAuth credentials belong in Git or CI.

Each run creates a labelled SQL Server container, a fresh NWFM_E2E database,
the API on 5081, local HTTP/SMTP transports on 5091/2525, and Angular on 4200.
All four ports must be free. Existing services are never reused or stopped.
Only the launcher-owned process groups and labelled container are cleaned up.
No business database, real email/SMS provider, or C2M service is used.
The development seeder's account is supplied by the wrapper via environment,
then resolved through e2e credentials; never put passwords in test instructions.

The example checks the login screen. The business test signs in, starts the
published Water isolation demo, completes three child/parent approvals, and checks
the root's completed state and history after a reload.

## Credential-free CI replay

Commit reviewed `.e2e/cache/*.json` recordings produced by successful local agent
runs. Do not edit them. All other reports, sessions and service data are ignored.

```sh
npm run test:e2e:replay
```

CI has no model or model credentials. e2e's custom executor interface allows
verified recordings to replay before execution; the fallback executor always
fails. Its identity matches the built-in executor of the pinned e2e 0.19.0 release.
Revalidate that compatibility whenever e2e is upgraded. Strict caching rejects
stale recordings, and missing recordings or unsupported replay cannot turn green
through a live model call. Tests use exact locator assertions rather than AI
judgments. The wrapper requires both test files, every discovered test to pass on the first
attempt, a fresh report, verified workflow actions, and zero model calls.

When a run fails, read the trace page named by the terminal. Fix the app or test;
do not add sleeps or weaken assertions. For a changed test, use `--no-cache`
to debug live behavior, then run normally to record, review the resulting cache,
and run replay against another fresh database.

## GitHub review and merging

The PR template is validated by CI. Fill every required section, choose a change
type, link an issue (or provide a concrete Not applicable explanation), and
complete testing/review checkboxes only when true.

Required checks: PR policy, Code quality, Frontend, Backend, Security, E2E.
Protection covers main and future develop: PRs, resolved conversations, current
base, and all six passing checks, with no administrator bypass. On 2026-10-12,
the owner explicitly chose solo review: zero required independent approvals and
no requirement for another person to approve the latest push or unattributed
changes. Optional reviews remain available; stale approvals are dismissed after
new changes. Force pushes and branch deletion remain blocked.

GitHub does not let a PR author approve their own PR. Under this policy the owner
reviews Files changed, then manually confirms the merge in GitHub when the PR is
ready and checks pass. This is an owner decision, not a recorded self-approval.
No separate approval comment or chat confirmation is enforced by GitHub.

Only the owner merges in GitHub. Agents must never merge, enable auto-merge,
push to protected branches, or weaken protections. Local hooks are bypassable;
GitHub is the enforcement boundary. An owner can still change repository settings,
and GitHub cannot distinguish a permitted UI merge from a permitted API merge.

The additional approval layer remains unspecified and is intentionally deferred.

## Rollout findings

The exposed Google API key has been removed from the current frontend config.
At the owner's request on 2026-10-12, `.gitleaksignore` accepts only its exact
historical commit/path/rule fingerprint. Revocation is unconfirmed; Git history
still contains the value. This exception does not allow future secret additions.
No history rewrite or protected-branch force push was performed.

The frontend now uses `@angular/build`, Vitest with the Playwright Chromium
provider, and Tailwind 4 with its PostCSS plugin. This removes every dependency
path to the unpatched `braces` advisory
[GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm).
Both npm audits report zero vulnerabilities. No advisory is suppressed, no
package is renamed or locally patched, and the high/critical security gate is unchanged.

All existing tests were migrated to Vitest without dropping assertions or test
cases. `npm run test:frontend` supplies the same installed Chromium used by e2e;
`CHROME_BIN` can select an alternative executable. The application remains on
Angular 21. Tailwind's theme now lives in `frontend/src/styles/global.css`;
`tailwind-compat.css` preserves the existing rounded, shadow, blur and outline
utility contract so component templates do not need business-logic edits.
Browser regression tests cover these utilities, brand colors, dark mode,
Arabic logical positioning, CSS variables and important modifiers.
Tailwind 4 requires Safari 16.4+, Chrome 111+ or Firefox 128+.

## Verification evidence

The rollout was checked with 694 backend tests, 235 frontend tests, production
builds, and six tooling tests. The tooling tests exercise actual Git hooks,
including partial staging, deletion-only commits, invalid messages, lint failures,
and failing unit commands.

The example and full workflow lifecycle pass against disposable seeded databases.
Strict replay passes all seven business actions without a model. A transient
recording was rejected with REPLAY_STALE before the final action was re-recorded
at a stable Completed state. Missing-recording and broken-assertion checks are
run separately during rollout; the normal suite is restored afterward.
