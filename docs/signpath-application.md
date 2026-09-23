# Applying to SignPath Foundation

[Code signing](signing.md) explains *why* SignPath Foundation and what the release workflow does
with a certificate once one exists. This page is the other half: everything the maintainer has to do
by hand, in the order it has to happen, with every answer already written.

Nothing here can be done by anyone but the repository's owner — applying, accepting terms and
holding an API token are acts of ownership. Read it top to bottom once before starting; the first
two sections change what you do and when.

---

## The order is fixed, and it starts with an unsigned release

> **Released**: the project must already be released in the form that should be signed.
>
> — [SignPath Foundation conditions](https://signpath.org/terms.html)

There is no way to apply first and release afterwards. The Foundation will not issue a certificate
for something nobody can download, so:

1. **v0.1.0 goes out unsigned.** Complete, published, installable — just unsigned. The release
   workflow already builds exactly that: with no `SIGNPATH_*` secrets every signing step skips and
   nothing errors.
2. **Then the application**, pointing at that release.
3. **Then, if accepted**, the configuration and secrets, and v0.2.0 (or v0.1.1) is the first signed
   release.

Everything below is in that order. Steps 1–4 are doable today; step 5 onward needs an acceptance
mail.

| # | Step | When |
|---|---|---|
| 1 | [Close the repository gaps](#step-1--close-the-repository-gaps) | now |
| 2 | [Release v0.1.0, unsigned](#step-2--release-v010-unsigned) | now, and before applying |
| 3 | [Publish the code signing policy](#step-3--publish-the-code-signing-policy) | with the application |
| 4 | [Apply](#step-4--apply) | after v0.1.0 exists |
| 5 | [Set the project up in SignPath](#step-5--set-the-project-up-in-signpath) | after acceptance |
| 6 | [Add the five repository secrets](#step-6--the-five-repository-secrets) | after step 5 |
| 7 | [Cut the first signed release](#step-7--the-first-signed-release) | after step 6 |

## What acceptance costs

Worth knowing before spending the effort, because neither of these is reversible by configuration:

* **Every release stops for a human.** "Every release needs manual approval for signing" is a
  condition of the programme, not a setting. The `Pack win` job blocks twice per release — once for
  the application's binaries, once for the installer — and waits for an approver to accept each
  request in SignPath's web UI. The workflow gives each 30 minutes before failing. A release cut at
  midnight is a release you get up to approve, or re-run in the morning.
* **Windows will name SignPath Foundation as the publisher, not you.** "The code signing certificate
  is issued to SignPath Foundation. This means that SignPath Foundation is the publisher of the OSS
  project." The UAC prompt and the file properties will say *SignPath Foundation*; they will not say
  Daniel Meza or KiCad Ultra. The Foundation can also pause the subscription or revoke the
  certificate if the project breaks its conditions.

Neither is a reason not to apply — free is the requirement and this is the only free route — but the
first one changes how releases feel, and the second is what a user sees.

---

## Step 1 — close the repository gaps

Each of these is a [condition](https://signpath.org/terms.html) the application is judged against.
The ones marked **done** are done in this repository as of this page; the rest are yours.

| Condition | Where it stands |
|---|---|
| No malware, no potentially unwanted programs | done — nothing here scans, exploits or hides |
| OSI-approved licence, no commercial dual-licensing | done — [MIT](../LICENSE), one licence, no dual |
| No proprietary components | done — every dependency is OSS or a system library |
| Actively maintained | done — and visibly so; keep the commit history and issue replies moving |
| Released in the form to be signed | **step 2** |
| Functionality documented on the download page | done — [README](../README.md) and the release notes |
| Code signing policy on the home page and download pages | **step 3** |
| Product name and version metadata on signed binaries | done — see [the metadata](#why-the-metadata-restriction-works) |
| Multi-factor authentication on every account with commit access | **yours to check** |
| Code signing roles assigned (authors, reviewers, approvers) | done in the policy text, step 3 |
| No hacking tools; uninstall provided; no silent system changes | done — Velopack registers an uninstaller |

Two of them need a word.

**Multi-factor authentication.** "All team members must use multi-factor authentication for both
SignPath and source code repository access (e.g. GitHub)." One account has commit access here, so
this is one checkbox: GitHub → Settings → Password and authentication → Two-factor authentication.
Do it before applying; it is the kind of thing a reviewer checks.

**A code of conduct.** The repository now has one ([`CODE_OF_CONDUCT.md`](../CODE_OF_CONDUCT.md),
Contributor Covenant 2.1, linked from the README). Note what it is and is not: the "Code of Conduct"
the Foundation's terms talk about is *the Foundation's own*, which is that terms page — a project
code of conduct is not an enumerated condition. It is still worth having, because the one thing the
Foundation weighs that cannot be checked off a list is whether a project looks real and cared-for.

**While you are in the repository settings**, give it a description. `gh repo view` currently returns
an empty `description`, and the GitHub page is the Homepage URL on the application form.

## Step 2 — release v0.1.0, unsigned

Nothing special: tag it as [Releasing](releasing.md) describes. Two things to check afterwards,
because the application points at this release and a reviewer will open it:

* the release carries the four Velopack channels, `SHA256SUMS.txt` and the PCM bundle — the workflow
  fails rather than publishing an incomplete one, so a green run is the check;
* the release notes, or the README the release links to, describe what the program does. That is the
  "Documented" condition ("The project's functionality must be described on its download page").

The binaries in this release will carry `0.1.0` as their product version, which is new: until the
change that added this page, every build said `1.0.0.0` whatever the tag. That matters later — see
[why the metadata restriction works](#why-the-metadata-restriction-works).

## Step 3 — publish the code signing policy

The terms want it in a specific place, in specific words:

> A code signing policy must be specified on the project's home page. Use the term "**Code signing
> policy**" on your project's home page and download/release pages (section header or link to a
> dedicated page).

**Publish it together with the application, not after acceptance.** The previous draft of
[signing.md](signing.md) said to wait until accepted, on the reasoning that it would otherwise claim
a certificate that does not exist. That is the wrong way round: the application form asks for a
Download URL and says that page "must provide signing information according to SignPath Foundation
Terms of Use", so the policy is part of what gets reviewed. The way out of the contradiction is to
publish it with the status on its face, which is what the variant below does, and to drop the status
clause the day the certificate is issued.

**Where.** As a `## Code signing policy` section of `README.md` (the home page), and on the releases
page. GitHub's releases index cannot carry a section, so the release *notes* are the place: once
signing is live, add a `body:` to the `softprops/action-gh-release` step in `release.yml` carrying
the two-line short form and a link back to the README section. (`generate_release_notes: true`
appends the generated notes below `body`; confirm that on the first signed release.)

### Variant A — publish this now, with the application

```markdown
## Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/) — **applied for, not yet granted.** Until it is, the
Windows artifacts in every release are unsigned; [Code signing](docs/signing.md) explains what that
means when you install one.

- **Roles.** Committers and reviewers: [@danielmeza](https://github.com/danielmeza). Approvers:
  [@danielmeza](https://github.com/danielmeza).
- **What will be signed.** The Windows build of the importer application (`KiCadUltra.exe` and
  `KiCadUltra.dll`) and its installer (`KiCadUltra-win-Setup.exe`), both built by
  [`release.yml`](.github/workflows/release.yml) from the source in this repository. Binaries
  belonging to upstream Open Source projects and redistributed inside the package — Velopack's
  updater and portable stub, the .NET runtime, the Chromium Embedded Framework — are not signed by
  this project, as the programme's terms require. The Linux and macOS artifacts are unsigned.
- **Privacy.** This program collects no personal data and has no telemetry, analytics or crash
  reporting. It contacts the network in two situations: when you search for a part, it queries the
  component sources listed in [Where the parts come from](docs/data-sources.md), with the
  credentials you entered for them if any; and it asks this repository's GitHub Releases feed
  whether a newer version exists — 20 seconds after it starts and every six hours after that —
  sending nothing but the request. The Web Browser tab is a browser: it contacts the sites you
  open in it. Settings, logs and imported files stay on your machine, and API tokens are kept in
  the operating system's credential store.
```

### Variant B — replace it with this once the certificate is issued

Identical, minus the status clause and the unsigned warning, and with "will be signed" in the
present tense:

```markdown
## Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

- **Roles.** Committers and reviewers: [@danielmeza](https://github.com/danielmeza). Approvers:
  [@danielmeza](https://github.com/danielmeza).
- **What is signed.** The Windows build of the importer application (`KiCadUltra.exe` and
  `KiCadUltra.dll`) and its installer (`KiCadUltra-win-Setup.exe`), both built by
  [`release.yml`](.github/workflows/release.yml) from the source in this repository. Binaries
  belonging to upstream Open Source projects and redistributed inside the package — Velopack's
  updater and portable stub, the .NET runtime, the Chromium Embedded Framework — are not signed by
  this project, as the programme's terms require. The Linux and macOS artifacts are unsigned.
- **Privacy.** *(unchanged from variant A)*
```

The privacy paragraph is written from what the code actually does, not from the Foundation's
suggested boilerplate. Their example sentence — "This program will not transfer any information to
other networked systems unless specifically requested by the user" — **would be untrue here**:
`AppUpdateService` asks GitHub for a newer release 20 seconds after startup and every six hours
after that, without anyone asking it to. Say what it does instead; the terms accept either a link to
a privacy policy or a statement, and an accurate statement is worth more than a tidy false one.

## Step 4 — apply

The form lives at [signpath.org/apply](https://signpath.org/apply). It is an embedded web form
today; the same fields are in the spreadsheet the site still serves at
[`/assets/OSSRequestForm-v4.xlsx`](https://signpath.org/assets/OSSRequestForm-v4.xlsx), which is
where the field names and the guidance quoted below come from. (Until early 2025 the route was to
fill in that spreadsheet and mail it to `oss-support@signpath.org`; if the web form is unavailable,
that is the fallback.)

Answers, ready to paste. `*` marks a required field.

| Field | Answer |
|---|---|
| Name `*` | `KiCad Ultra` |
| Handle `*` | `kicad-ultra` |
| Type `*` | `Program` |
| License `*` | `MIT License` — `https://opensource.org/license/mit` |
| Repository URL `*` | `https://github.com/danielmeza/kicad-ultra` |
| Homepage URL `*` | `https://github.com/danielmeza/kicad-ultra` |
| Download URL | `https://github.com/danielmeza/kicad-ultra/releases` |
| Privacy Policy URL | `https://github.com/danielmeza/kicad-ultra#code-signing-policy` |
| Wikipedia URL | *(none)* |
| Tagline `*` | `Search for a part and import its symbol, footprint and 3D model into KiCad` |
| User Full Name `*` | `Daniel Meza` |
| User Email `*` | `<maintainer email>` |
| Build System `*` | `GitHub Actions` |
| Accept terms of use `*` | `I hereby accept the terms of use` |

**Description** `*` — one paragraph, and the form asks that it not need rewriting when a major
version ships, so it names no individual data source or feature:

```text
KiCad Ultra is a plugin for the KiCad EDA suite that brings component search and library import
inside the editor. It searches component distributors and CAD-model providers for a part and
imports the chosen part's schematic symbol, footprint and 3D model into the open project's
libraries, registering them in KiCad's library tables so the part is usable straight away. The
plugin is a small Python package that KiCad loads; the importer it launches is a cross-platform
desktop application written in C# on .NET and Avalonia, which talks to the running KiCad over its
IPC API and updates itself from this repository's releases.
```

**Reputation** `*` — the field that decides this, and the one where an honest answer is thin. The
terms are blunt about why it exists: "we cannot sign binaries based on source code that nobody
knows. For executable programs that may be downloaded and executed based on our signature, we
require a certain verifiable reputation." What there is to point at:

```text
The project is developed in the open at https://github.com/danielmeza/kicad-ultra, with its
design decisions, measurements and open questions recorded in its issues and pull requests rather
than only in commits.

It is published through KiCad's own Plugin and Content Manager, the distribution channel KiCad
users install add-ons from.

Two libraries written for this project are published separately on nuget.org and are used by it as
ordinary packages: SExpressions (https://www.nuget.org/packages/SExpressions) and KiCadSharp
(https://www.nuget.org/packages/KiCadSharp), an IPC client and file-format library for KiCad.

Every release is built by a GitHub Actions workflow in the repository, on GitHub-hosted runners,
from the tagged source; nothing is uploaded from a developer machine.
```

Then add, in your own words, whatever is true by the time you apply: downloads on the v0.1.0
release, PCM installs, stars and forks, any forum or blog mention. Today the repository has a
single star and one fork, and that is the honest weak point of this application — **expect it to be
the reason if the answer is no.** "We're under no obligation to accept your project, and there is
no independent arbitration mechanism." A refusal is not a verdict on the software; it means come
back with more of a track record.

Two more things to expect on the form:

* **A short URL with your handle goes into the certificate** — `sig.fo/kicad-ultra` — so the handle
  is not a throwaway.
* **The name is checked for ambiguity and for trademarks you do not own**: "Do not use the name of
  another project or a trademark you don't own." *KiCad* is a trademark held by the Linux
  Foundation, and "KiCad Ultra" uses it. That is ordinary nominative use for a plugin — the name
  says what the plugin works with, the README already states that the project is not affiliated
  with or endorsed by the projects it names, and KiCad publishes no third-party naming policy that
  forbids it. But it is the second question a reviewer might ask, so have the answer ready, and be
  prepared to qualify the name (the form suggests your own name or handle as a prefix or suffix) if
  they push back.

There is **no published turnaround time**, from SignPath or from anyone who has written up the
process. Do not plan a release around a date.

---

*Everything below happens after an acceptance mail.*

## Step 5 — set the project up in SignPath

Four objects, in this order. Note each slug as you go; four of them become repository secrets.

**1. The project.** Create it for this repository. Its **slug** is what
`SIGNPATH_PROJECT_SLUG` holds (`kicad-ultra`). The **organization ID** — a GUID, on the organization
page — is `SIGNPATH_ORGANIZATION_ID`.

Link the predefined **Trusted Build System** entry *GitHub.com* to the project. Without it the
connector cannot verify that a signing request came from a GitHub workflow rather than from someone
holding the API token.

**2. The artifact configuration.** `release.yml` uploads a ZIP holding only the files it means to
sign — the application's `.exe` and `.dll` in the first round, the installer in the second — so one
configuration serves both. `actions/upload-artifact@v4` always produces a ZIP, which is why the root
element is `<zip-file>`.

```xml
<artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
  <!-- The release workflow passes the tag's version with the signing request, so a request whose
       binaries do not carry that exact version is rejected rather than signed. -->
  <parameters>
    <parameter name="version" required="true" />
  </parameters>
  <zip-file>
    <pe-file-set product-name="KiCad Ultra"
                 product-version="${version}"
                 company-name="Daniel Meza">
      <!-- Round 1: KiCadUltra.exe + KiCadUltra.dll. Round 2: KiCadUltra-win-Setup.exe.
           Wildcards rather than names, so a rename does not have to be applied here on the
           same day. -->
      <include path="*.exe" max-matches="unbounded" />
      <include path="*.dll" min-matches="0" max-matches="unbounded" />
      <for-each>
        <authenticode-sign />
      </for-each>
    </pe-file-set>
  </zip-file>
</artifact-configuration>
```

The three restrictions are the terms' "All signed binaries must have metadata attributes set and
enforced using file metadata restrictions". Three others that SignPath offers are deliberately
**not** used, each for a measured reason — see [below](#why-the-metadata-restriction-works).

**3. The signing policy.** Call it `release-signing`; its slug is
`SIGNPATH_SIGNING_POLICY_SLUG`. Add yourself as an **approver** — this is the manual approval the
programme requires, and without an approver every release hangs until it times out.

**4. The CI user.** A user added as a **submitter** on that signing policy, with an **API token**.
The token is `SIGNPATH_API_TOKEN`, and it is the only one of the five that is a credential.

## Step 6 — the five repository secrets

GitHub → Settings → Secrets and variables → Actions → *New repository secret*.

| Secret | Value | Required |
|---|---|---|
| `SIGNPATH_API_TOKEN` | the CI user's API token | yes |
| `SIGNPATH_ORGANIZATION_ID` | the organization's GUID | yes |
| `SIGNPATH_PROJECT_SLUG` | `kicad-ultra` | yes |
| `SIGNPATH_SIGNING_POLICY_SLUG` | `release-signing` | yes |
| `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | the artifact configuration's slug | no — empty selects the project's default |

Only the first is a credential; the rest are identifiers. They are secrets anyway so that *all* of
them are absent in a fork, which is how the workflow decides to build unsigned rather than fail
against an organization it cannot reach. Nothing in the workflow prints a secret's value — the gate
step names only the ones that are missing.

## Step 7 — the first signed release

Push a `v*` tag as usual. Then:

1. **Watch for two signing requests** in SignPath, one for the application binaries and one for the
   installer. Approve both. The job waits 30 minutes for each; a timeout fails the job before
   anything is published, so a missed approval costs a re-run, not a bad release.
2. **The workflow checks the signatures itself** before publishing — it reads the application's two
   binaries back out of the package that `releases.win.json` points at, plus `Setup.exe`, and fails
   if any of them has an empty PE certificate table. That is the failure a green signing step does
   not catch: a request that matches nothing still succeeds and hands back what it was given.
3. **Check it by hand once**, on Windows:

   ```powershell
   Get-AuthenticodeSignature .\KiCadUltra-win-Setup.exe | Format-List Status, SignerCertificate
   ```

   `Status` must be `Valid`, and the subject should read `SignPath Foundation`.
4. **Swap the code signing policy to [variant B](#variant-b--replace-it-with-this-once-the-certificate-is-issued)**
   and add it to the release notes.

A `workflow_dispatch` run signs too, when the secrets are set. That is the only way to rehearse the
wiring without publishing a release; deny the requests in SignPath if you did not mean to spend
them.

---

## Why the metadata restriction works

The terms require that signed binaries carry a product name and a product version and that the
artifact configuration enforce them. Until the change that added this page, the binaries carried
neither: no `<Product>` or `<Version>` was set anywhere, so every build shipped as `1.0.0.0` under
the assembly name, whatever the release was tagged. `Directory.Build.props` now sets `$(Product)`
and `$(Company)`, and `release.yml` passes the tag as `-p:Version=`.

One more property had to be turned off for that to hold. The .NET SDK has bundled SourceLink since
.NET 8, so in a git checkout it appends the commit to the informational version — and the
informational version is exactly what a PE file reports as `ProductVersion`. Measured on this
application before the change, a `0.1.0` build called itself
`0.1.0+2507afb0909dd1333fa4eb5774bb28a0d297a376`, while Velopack's installer called itself `0.1.0`:
one release, two versions, and a `product-version` restriction that would reject every signing
request. `IncludeSourceRevisionInInformationalVersion` is now `false`, with that reason on it.

What ends up in the files, measured by publishing this application for `win-x64` at `0.1.0` and by
packing a throwaway Velopack package with vpk 1.2.158, then reading the PE version resources:

| | `KiCadUltra.exe` / `.dll` | `KiCadUltra-win-Setup.exe` |
|---|---|---|
| Where the metadata comes from | `$(Product)`, `$(Company)`, `$(Version)` in the build | `--packTitle`, `--packAuthors`, `--packVersion` in `vpk pack` |
| `ProductName` | `KiCad Ultra` | `KiCad Ultra` |
| `ProductVersion` | `0.1.0` | `0.1.0` |
| `CompanyName` | `Daniel Meza` | `Daniel Meza` |
| `FileVersion` | `0.1.0.0` | `0.1.0` |
| `OriginalFilename` | `KiCadUltra.dll` (for **both** files) | *(none)* |
| `LegalCopyright` | *(unset)* | `Copyright © 2026 Daniel Meza` |

The first three rows agree across every signed file, which is what makes one artifact configuration
and one set of restrictions possible. The last three are why `file-version`, `original-filename` and
`copyright` restrictions are left off: each has a different value in the installer than in the
application, or the same value in two files that are not the same file, so restricting them would
reject every signing request.

The consequence is a rule: **`--packTitle` must keep reading exactly like `$(Product)`, and
`--packAuthors` like `$(Company)`.** Both files carry a comment saying so.

## What is verified here, and what is not

Verified while writing this, against the live sources:

* the conditions, quoted from [signpath.org/terms.html](https://signpath.org/terms.html) as it
  stands today;
* the application's fields, from the OSS request form the site still serves;
* the artifact configuration syntax and the list of PE metadata restrictions
  (`product-name`, `product-version`, `file-version`, `company-name`, `copyright`,
  `original-filename`), from SignPath's own reference and examples;
* that `actions/upload-artifact@v4` produces a ZIP, hence the `<zip-file>` root, and that the
  GitHub connector needs `actions: read` and `contents: read` — both already in `release.yml`;
* the metadata table above: the application's two columns by publishing this project for `win-x64`
  at `0.1.0` and parsing the version resources of `KiCadUltra.exe` and `KiCadUltra.dll`, the
  installer's by packing a throwaway application with vpk 1.2.158 and parsing `Setup.exe`;
* that `-p:Version=` drives `AssemblyVersion`, `FileVersion` and `InformationalVersion` together,
  and that `ProductVersion` in the PE resource is the informational one — which is why it reads
  `0.1.0` and not `0.1.0.0`, and why the SourceLink suffix above mattered.

Not verified, and not verifiable without a certificate: that the application is accepted, that the
artifact configuration matches what CI uploads, that the signed files come back under the same
names, that an approval fits inside 30 minutes, and what Windows shows for the publisher. The first
signed release is the test.
