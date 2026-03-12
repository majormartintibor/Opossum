# Documentation Website Plan — DocFX + GitHub Pages

**Date:** 2025-06-11  
**Status:** ✅ Implemented — 2025-06-11  
**Chosen tool:** [DocFX](https://dotnet.github.io/docfx/)  
**Target host:** GitHub Pages (`gh-pages` branch)

---

## Overview

Opossum will have a public documentation website built with DocFX and hosted on GitHub Pages.  
The site source lives inside this solution under `docs/docfx/` and is built and deployed automatically via GitHub Actions on every push to `master`.

The site serves two audiences:

| Audience | What they need |
|---|---|
| **Library consumers** | Getting-started guide, API reference, concept explanations, use-case examples |
| **Contributors** | Architecture decisions, specifications, implementation notes |

---

## Why DocFX

See the full comparison in the parent brainstorm. Summary of why DocFX wins for Opossum:

| Criterion | DocFX | VitePress | Jekyll |
|---|---|---|---|
| API reference from XML comments | ✅ Automatic | ❌ Manual | ❌ Manual |
| Existing Markdown reused as-is | ✅ | ✅ | ✅ |
| .NET-native tooling (`dotnet tool`) | ✅ | ❌ (Node.js) | ❌ (Ruby) |
| GitHub Pages deployment | ✅ | ✅ | ✅ Native |
| No foreign runtime in CI | ✅ | ❌ | ❌ |
| Actively maintained | ✅ (Microsoft) | ✅ | ⚠️ Slowing |

The auto-generated API reference is the decisive factor — it surfaces every public type and member from `src/Opossum/` with zero extra authoring effort, and keeps API docs in sync with the code automatically.

---

## Proposed Repository Structure

```
docs/
  docfx/                          ← DocFX project root
    docfx.json                    ← main configuration
    index.md                      ← site landing page (new — polished README)
    toc.yml                       ← top-level navigation bar
    articles/
      toc.yml                     ← sidebar nav for all article sections
      getting-started/
        toc.yml
        installation.md           ← NEW: install via NuGet, prerequisites
        quick-start.md            ← NEW: 5-minute "hello events" walkthrough
        configuration.md          ← maps to: docs/configuration-guide.md
      concepts/
        toc.yml
        event-store.md            ← NEW: what is an event store, Opossum's model
        dcb.md                    ← maps to: docs/specifications/DCB-Specification.md
        projections.md            ← maps to: docs/specifications/dcb-projections.md
        mediator.md               ← NEW: mediator pattern in Opossum
      guides/
        toc.yml
        use-cases.md              ← maps to: docs/guides/use-cases.md
        durability.md             ← maps to: docs/guides/durability-quick-reference.md
        configuration-validation.md ← maps to: docs/configuration-validation.md
      decisions/
        toc.yml                   ← only the public-facing subset of docs/decisions/
    images/
      opossum.png                 ← copied from Solution Items/opossum.png
.github/
  workflows/
    docs.yml                      ← NEW: build + deploy workflow
```

> **Design principle:** `docs/docfx/articles/` contains either new articles or *copies*  
> of existing docs from `docs/`. Duplicating rather than symlinking avoids DocFX's  
> limited cross-directory linking support and makes the site content self-contained.  
> When an existing doc is copied, note the source at the top with an HTML comment:  
> `<!-- source: docs/guides/use-cases.md — keep in sync -->`

---

## `docfx.json` Specification

```jsonc
{
  // ── Metadata (API reference) ─────────────────────────────────────────────
  "metadata": [
    {
      "src": [
        {
          // Point at the library csproj; DocFX resolves XML doc comments
          "files": [ "src/Opossum/Opossum.csproj" ],
          "src": "../.."            // relative to docs/docfx/
        }
      ],
      "dest": "api",
      "disableGitFeatures": false,
      "disableDefaultFilter": false
    }
  ],

  // ── Build (static site) ───────────────────────────────────────────────────
  "build": {
    "content": [
      // Auto-generated API YAML files
      {
        "files": [ "api/**.yml", "api/index.md" ]
      },
      // Conceptual articles and nav files
      {
        "files": [
          "index.md",
          "toc.yml",
          "articles/**/*.md",
          "articles/**/toc.yml"
        ]
      }
    ],

    "resource": [
      { "files": [ "images/**" ] }
    ],

    "output": "_site",

    // "modern" is DocFX's current default responsive theme
    "template": [ "default", "modern" ],

    "globalMetadata": {
      "_appName": "Opossum",
      "_appTitle": "Opossum — File System Event Store",
      "_appFooter": "Opossum — MIT License",
      "_enableSearch": true,
      "_gitContribute": {
        "repo": "https://github.com/majormartintibor/Opossum",
        "branch": "master",
        "apiSpecFolder": "docs/docfx/api"
      }
    },

    "fileMetadata": {},
    "postProcessors": [],
    "markdownEngineProperties": {
      "markdigExtensions": [ "abbreviation", "definitionlists" ]
    }
  }
}
```

---

## Navigation Structure

### Top-level `toc.yml`

```yaml
- name: Getting Started
  href: articles/getting-started/toc.yml
  topicHref: articles/getting-started/installation.md

- name: Concepts
  href: articles/concepts/toc.yml
  topicHref: articles/concepts/event-store.md

- name: Guides
  href: articles/guides/toc.yml
  topicHref: articles/guides/use-cases.md

- name: Architecture Decisions
  href: articles/decisions/toc.yml

- name: API Reference
  href: api/
```

### `articles/getting-started/toc.yml`

```yaml
- name: Installation
  href: installation.md
- name: Quick Start
  href: quick-start.md
- name: Configuration
  href: configuration.md
```

### `articles/concepts/toc.yml`

```yaml
- name: What is an Event Store?
  href: event-store.md
- name: DCB Specification
  href: dcb.md
- name: Projections
  href: projections.md
- name: Mediator Pattern
  href: mediator.md
```

### `articles/guides/toc.yml`

```yaml
- name: Use Cases
  href: use-cases.md
- name: Durability Guarantees
  href: durability.md
- name: Configuration Validation
  href: configuration-validation.md
```

### `articles/decisions/toc.yml`

```yaml
- name: ADR-001 — ConfigureAwait
  href: adr-001.md
- name: ADR-003 — IAsyncEnumerable
  href: adr-003.md
- name: ADR-004 — Single Context
  href: adr-004.md
- name: ADR-005 — Cross-Process Safety
  href: adr-005.md
```

> ADR-002 (NuGet release assessment) is an internal operations doc and is intentionally  
> excluded from the public site.

---

## Content Plan

### New articles to author (do not yet exist)

| File | Description |
|---|---|
| `docs/docfx/index.md` | Site landing page — elevator pitch, badges, install snippet, link to quick start |
| `articles/getting-started/installation.md` | Prerequisites (.NET 8+), NuGet install command, project reference |
| `articles/getting-started/quick-start.md` | Minimal working example: register → append → read → project |
| `articles/concepts/event-store.md` | What is an event store, how Opossum stores events on disk |
| `articles/concepts/mediator.md` | Opossum's mediator pattern, command/query handlers, DI wiring |

### Existing docs to copy into the site

| Source file | Destination in `docs/docfx/articles/` | Notes |
|---|---|---|
| `docs/configuration-guide.md` | `getting-started/configuration.md` | Remove internal notes, keep public content |
| `docs/specifications/DCB-Specification.md` | `concepts/dcb.md` | Copy as-is |
| `docs/specifications/dcb-projections.md` | `concepts/projections.md` | Copy as-is |
| `docs/guides/use-cases.md` | `guides/use-cases.md` | Copy as-is |
| `docs/guides/durability-quick-reference.md` | `guides/durability.md` | Copy as-is |
| `docs/configuration-validation.md` | `guides/configuration-validation.md` | Copy as-is |
| `docs/decisions/001-configureawait-implementation.md` | `decisions/adr-001.md` | Copy as-is |
| `docs/decisions/003-iasyncenumerable-not-implemented.md` | `decisions/adr-003.md` | Copy as-is |
| `docs/decisions/004-single-context-by-design.md` | `decisions/adr-004.md` | Copy as-is |
| `docs/decisions/005-cross-process-append-safety.md` | `decisions/adr-005.md` | Copy as-is |

---

## XML Documentation Comments

For the API reference to be useful, all public members in `src/Opossum/` need `///` XML doc comments.  
Before publishing the site, audit the following as a minimum:

- [ ] All public interfaces (`IEventStore`, `IProjection`, etc.)
- [ ] All public DTOs / record types (`AppendCondition`, `SequencedEvent`, etc.)
- [ ] All public configuration types (`OpossumOptions`, etc.)
- [ ] The DI extension method (`ServiceCollectionExtensions.AddOpossum`)
- [ ] All public exception types

DocFX will generate stubs for undocumented members but they will show only signatures — no descriptions.  
An `<inheritdoc />` tag on implementing classes will pull comments from the interface.

---

## GitHub Actions Workflow

New file: `.github/workflows/docs.yml`

```yaml
name: Documentation

on:
  push:
    branches:
      - master
  workflow_dispatch:        # allow manual trigger

# Allow write access to deploy to gh-pages
permissions:
  contents: write

jobs:
  build-and-deploy:
    name: Build & Deploy Docs
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4
        with:
          fetch-depth: 0    # full history for git metadata in DocFX

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Install DocFX
        run: dotnet tool install -g docfx

      - name: Build site
        run: docfx docs/docfx/docfx.json

      - name: Deploy to GitHub Pages
        uses: peaceiris/actions-gh-pages@v4
        with:
          github_token: ${{ secrets.GITHUB_TOKEN }}
          publish_dir: docs/docfx/_site
          publish_branch: gh-pages
          cname: ""           # fill in custom domain if one is registered later
```

> The `gh-pages` branch is auto-created by `peaceiris/actions-gh-pages` on first run.  
> After the first successful deployment, enable GitHub Pages in the repository  
> Settings → Pages → Source: `gh-pages` branch, root `/`.

---

## Local Development

### Prerequisites

```powershell
# Install DocFX as a global .NET tool (once)
dotnet tool install -g docfx

# Verify
docfx --version
```

### Serve locally with live reload

```powershell
# From repo root
docfx docs/docfx/docfx.json --serve

# Site available at http://localhost:8080
```

### Build only (no server)

```powershell
docfx docs/docfx/docfx.json
# Output: docs/docfx/_site/
```

`docs/docfx/_site/` is git-ignored — it is a build artefact.

---

## `.gitignore` additions required

```
# DocFX build output
docs/docfx/_site/
docs/docfx/api/
```

> `docs/docfx/api/` contains auto-generated YAML; it is regenerated on every build  
> and should not be committed.

---

## GitHub Repository Settings (post-deployment)

1. Go to **Settings → Pages**
2. Set **Source** to `Deploy from a branch`
3. Set **Branch** to `gh-pages`, folder `/`
4. Optionally configure a custom domain

The live URL will be: `https://majormartintibor.github.io/Opossum/`

---

## Implementation Checklist

When implementing, complete tasks in this order:

### Phase 1 — Scaffold

- [ ] Add `docs/docfx/_site/` and `docs/docfx/api/` to `.gitignore`
- [ ] Create `docs/docfx/docfx.json` (from spec above)
- [ ] Create `docs/docfx/index.md` — landing page
- [ ] Create all `toc.yml` files (from spec above)
- [ ] Add `images/opossum.png`
- [ ] Verify `docfx docs/docfx/docfx.json --serve` runs without errors

### Phase 2 — Content

- [ ] Copy existing docs to `articles/` (see content plan table)
- [ ] Author `installation.md`
- [ ] Author `quick-start.md`
- [ ] Author `event-store.md`
- [ ] Author `mediator.md`

### Phase 3 — API Reference quality

- [ ] Audit XML doc comments on all public interfaces
- [ ] Audit XML doc comments on all public types and records
- [ ] Audit XML doc comments on `AddOpossum` DI extension
- [ ] Verify generated API pages look correct locally

### Phase 4 — CI/CD

- [ ] Create `.github/workflows/docs.yml` (from spec above)
- [ ] Push to `master` and verify workflow succeeds
- [ ] Enable GitHub Pages in repository settings
- [ ] Verify site is live at `https://majormartintibor.github.io/Opossum/`

### Phase 5 — Polish

- [ ] Review all pages for broken links (`docfx` reports these as warnings)
- [ ] Add site URL to `README.md` badge row
- [ ] Add site URL to NuGet package metadata (`<PackageProjectUrl>`) in `Opossum.csproj`
- [ ] Update `CHANGELOG.md`
