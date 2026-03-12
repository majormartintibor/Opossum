# Future Plan: SonarCloud Quality Gate + Copilot Autofix in CI/CD

> **Status:** Brainstorm / Not yet implemented  
> **Topic:** Automated code quality enforcement with AI-assisted remediation

---

## Vision

Integrate a **SonarCloud Quality Gate** into the existing GitHub Actions CI/CD pipeline
(`ci.yml`) so that:

- **Low / Medium severity issues** found by Sonar are automatically fixed by GitHub Copilot
  and committed as an auto-generated pull request (no human investigation needed).
- **High severity issues** block the pipeline and require explicit human review and approval.

---

## Proposed Architecture

### The SARIF Bridge

The most realistic native path uses **SARIF** (Static Analysis Results Interchange Format)
as the glue between SonarCloud and GitHub's tooling:

```
Push / PR
  ↓
SonarCloud analysis runs
  ↓
Results exported as SARIF
  ↓
SARIF uploaded to GitHub Code Scanning
  ↓
Copilot Autofix suggests fixes on Code Scanning alerts
  ↓
Low / Medium  →  auto-apply fix  →  run tests  →  open auto-PR
High          →  open GitHub Issue  →  notify  →  block merge
```

### Custom Workflow Steps (where native support is incomplete)

1. Sonar scan completes in CI.
2. A GitHub Action calls the **SonarCloud API** to fetch open issues.
3. Filter issues by severity: `LOW` or `MEDIUM`.
4. For each issue, call the **Copilot API / Models API** with the affected file and issue
   description to generate a suggested fix.
5. Apply the patch locally, then run `dotnet build` and `dotnet test`.
6. If all tests pass → commit to a dedicated branch and open a pull request automatically.
7. `HIGH` severity issues → open a GitHub Issue, post a PR comment, and fail the quality gate.

---

## What Is Available Today vs. What Needs Custom Work

| Capability | Available Today | Notes |
|---|---|---|
| SonarCloud Quality Gate blocking PRs | ✅ | Native SonarCloud + GitHub integration |
| SonarCloud SARIF export to GitHub Code Scanning | ✅ | Supported via upload-sarif action |
| Copilot Autofix *suggesting* fixes on alerts | ✅ | Works for security alerts today |
| Copilot Autofix *auto-committing* without human | ⚠️ | Not natively supported — requires a human click |
| Full auto-PR workflow for low/medium issues | 🔧 | Needs custom GitHub Actions implementation |

---

## Prerequisites

1. **SonarCloud account** — free for public repositories at [sonarcloud.io](https://sonarcloud.io).
2. **GitHub secrets:**
   - `SONAR_TOKEN` — generated on SonarCloud.
   - `GITHUB_TOKEN` — already available automatically in Actions.
3. **Code coverage** — tests need to emit coverage data:
   - Add `--collect:"XPlat Code Coverage"` to `dotnet test` calls.
   - Merge reports with `reportgenerator` before uploading to Sonar.
4. **SonarScanner for .NET** — a CLI tool (not a NuGet package), so it does not conflict
   with the project's "no external libraries in `src/`" rule.

---

## Key Risks and Mitigations

| Risk | Mitigation |
|---|---|
| AI fix introduces a subtle bug | Full test suite (`dotnet test`) must pass before auto-PR is created — the test suite acts as the safety net |
| Low/medium issue is intentional (e.g. conscious naming choice) | PR description includes the Sonar rule ID and rationale; author can close/reject with a comment |
| Auto-merging AI commits to `master` removes second pair of eyes | Auto-PRs target a dedicated branch; a human still clicks **Merge** (one click, no investigation needed) |
| Copilot API not designed for CI automation | May need to use GitHub Models API or a separate LLM endpoint as fallback |

---

## Recommended Safe Variant

Rather than fully automatic commits, a pragmatic middle ground that preserves safety:

- **Low / Medium** → Copilot opens a PR with the suggested fix; tests must pass; **human clicks Merge** (one click, no debugging).
- **High** → GitHub Issue created, PR blocked, full human review required.

This captures most of the automation benefit while keeping humans in the final decision loop.

---

## Notes for Implementation

- The Sonar scan must run on `ubuntu-latest` only (not the Windows matrix leg) to avoid
  double billing / duplicate SARIF uploads.
- `.NET 10` is supported by recent versions of the SonarScanner for .NET.
- Coverage merging is needed because the solution has two separate test projects
  (`Opossum.UnitTests` and `Opossum.IntegrationTests`).

---

## References

- [SonarCloud GitHub Integration](https://docs.sonarcloud.io/getting-started/github/)
- [GitHub Code Scanning — upload-sarif action](https://docs.github.com/en/code-security/code-scanning/integrating-with-code-scanning/uploading-a-sarif-file-to-github)
- [GitHub Copilot Autofix](https://docs.github.com/en/code-security/code-scanning/managing-code-scanning-alerts/about-autofix-for-codeql)
- [SonarScanner for .NET](https://docs.sonarcloud.io/advanced-setup/ci-based-analysis/sonarscanner-for-net/)
