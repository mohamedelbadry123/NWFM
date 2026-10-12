# Repository workflow

- Never merge pull requests, enable auto-merge, or push directly to main or develop. The user reviews the changes and performs the final merge in GitHub after all required checks pass. The owner explicitly chose zero required independent approvals.
- Never bypass hooks, required checks, reviews, or branch protections; never weaken protections to finish a task.
- Use a feature branch. Preserve unrelated and partially staged changes.
- Run the repository quality checks, complete unit suites, and builds before presenting a PR. Report failures honestly.
- End-to-end tests use e2e; read .agents/skills/e2e/SKILL.md and the relevant topic before writing or running one.
- Use only disposable test databases and local integration transports for tests.
- A ChatGPT sign-in must be confirmed by the user before authenticated model calls. Never copy OAuth credentials to CI.
- The user's additional approval layer has not yet been specified. Do not invent or implement it.
