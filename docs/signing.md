# Code signing

The importer ships as a self-contained Windows build that a user downloads and runs, and that then
replaces its own binaries through Velopack. Unsigned, Windows SmartScreen warns about the installer
and Microsoft Defender has nothing to check the download against.

This page is the whole story: what the release workflow signs today, what it deliberately does not,
which programme was chosen and why, and — the part only the maintainer can do — what to apply for,
in what order, and what to paste into repository secrets.

**Nothing here is done yet.** No certificate exists, no application has been made, and no secret is
set. The workflow is wired and skips cleanly until all four secrets are present, so every release
built before then is a complete, unsigned release.

## What the release workflow does

`.github/workflows/release.yml` signs the Windows artifacts through
[SignPath Foundation](https://signpath.org/), in two rounds, and only when the four `SIGNPATH_*`
secrets exist:

| File | Signed | When |
|---|---|---|
| `UltraLibrarianImporter.UI.exe` (the apphost a user runs) | yes | before `vpk pack` |
| `UltraLibrarianImporter.UI.dll` (the application itself) | yes | before `vpk pack` |
| `KiCadUltra-win-Setup.exe` (the installer) | yes | after `vpk pack` |
| `Squirrel.exe` / `Update.exe` (Velopack's updater) | **no** | — |
| `KiCadUltra_ExecutionStub.exe` (the portable launcher) | **no** | — |
| the .NET runtime, CEF, Avalonia and everything else in the package | **no** | — |
| the Linux AppImage and both macOS packages | **no** | — |

Two rules decide that split, and neither is negotiable.

**The application's binaries have to be signed before packing.** `vpk pack` writes
`releases.win.json` with the SHA-256 of the `.nupkg`, and Velopack checks that hash on every update
and rebuilds the package from deltas diffed against it. Change a byte of the `.nupkg` after packing
and updating breaks for everyone who already installed. Signing the files that go *into* it is free
of that problem — the hash is computed over the signed bytes. The installer is the opposite case:
`releases.win.json` lists the `.nupkg` and nothing else (checked against vpk 1.2.158 by packing a
throwaway application), so signing `Setup.exe` afterwards changes nothing Velopack reads.

**Velopack's own binaries must not be signed by this project.** SignPath Foundation's conditions say
"The team must only sign software artifacts built from their own source code", and in the same
section allow shipping upstream binaries as they are: "You may include unsigned binaries of upstream
OSS projects, e.g. DLL files, in your signed packages"
([terms](https://signpath.org/terms.html)). `Squirrel.exe` and the execution stub are Velopack's
prebuilt binaries, copied into the package by `vpk`; the .NET runtime is Microsoft's and already
carries Microsoft's signature. Re-signing any of them with a SignPath Foundation certificate would
breach the programme.

That leaves one real gap: someone who downloads `KiCadUltra-win-Portable.zip` by hand and
double-clicks `KiCadUltra.exe` runs Velopack's unsigned stub. The plugin's own path does not — the
launcher runs `current/UltraLibrarianImporter.UI.exe` inside the extracted archive directly, and
that file is signed. Closing the gap needs a signing route that `vpk` can call per file; see
[If the project ever pays for signing](#if-the-project-ever-pays-for-signing).

### Two things signing does not do

* **It does not make the updater verify anything.** Velopack does not check Authenticode on an
  update. What protects the update path is the HTTPS release feed and the SHA-256 in
  `releases.{channel}.json`, plus `SHA256SUMS.txt` for the plugin's first-run download. Signing
  makes Windows trust the file a *user* launched; it does not add a check inside the updater.
* **It does not buy instant SmartScreen clearance.** Microsoft's own guidance is that reputation
  builds over time for OV certificates and for Azure Artifact Signing alike, and that EV
  certificates stopped bypassing SmartScreen in 2024
  ([Code signing options for Windows app developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)).
  What matters is signing every release with the same identity so reputation accumulates instead of
  restarting.

## Why SignPath Foundation

| Option | Cost | What the maintainer does | Unattended CI | Fits Velopack |
|---|---|---|---|---|
| **SignPath Foundation** | free for qualifying OSS ([signpath.org](https://signpath.org/)) | apply, publish a code signing policy, approve every release by hand | signing runs from CI; **each release needs a human approval** | signs uploaded artifacts, so pre-pack + post-pack rounds (what this repo does) |
| SignPath.io free OSS subscription, own certificate | free subscription, certificate costs whatever a CA charges | buy and hold a certificate | yes | same shape |
| **Azure Artifact Signing** (formerly Trusted Signing) | ~$9.99/month ([Microsoft](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)) | Azure subscription, identity validation (1–20 business days) | fully unattended | **best fit** — `vpk pack --azureTrustedSignFile` / `--signTemplate` signs every file including the stub, the updater and `Setup.exe` |
| **Certum Open Source Code Signing** | from €69 gross ([Certum shop](https://shop.certum.eu/open-source-code-signing.html)) | buy, verify identity, receive a cryptographic card and reader by courier | **no** — the key is on a physical smart card | would need a self-hosted runner with the card plugged in |
| OV certificate from a commercial CA | $150–300/year ([Microsoft](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)) | buy, verify, manage an HSM or token | depends on the CA's cloud HSM | via `--signTemplate` |

Free was the requirement, and SignPath Foundation is the only free route: Microsoft's own code
signing page sends open source projects there, and it is the only programme that provides the
*certificate* rather than only a place to keep one.

**The free tier and the Foundation programme are different things**, and the terms page is explicit
about the boundary: the conditions are split into "Conditions for free OSS SignPath.io
subscriptions" and "Conditions for free code signing certificates from SignPath Foundation", with
the line "If you bring your own certificate, that's it. If your certificate is issued by SignPath
Foundation, see the next section for additional constraints."
([terms](https://signpath.org/terms.html)) A free platform subscription without a certificate is no
use here, so this project wants both halves.

**What accepting the Foundation certificate means.** "The code signing certificate is issued to
SignPath Foundation. This means that SignPath Foundation is the publisher of the OSS project."
Windows will show **SignPath Foundation** as the verified publisher, not Daniel Meza and not KiCad
Ultra. The Foundation can pause the subscription or revoke the certificate if its Code of Conduct is
breached, and every release must be approved by a named approver.

## What the maintainer has to do, in order

Nobody but the maintainer can do any of this: applying, accepting the terms and holding an API token
are all acts of the project's owner.

### 1. Make the repository meet the conditions

All of these are [SignPath Foundation conditions](https://signpath.org/terms.html), and the
application is judged against them.

- [ ] **Two-factor authentication** on the GitHub account, and on every account with commit access
      ("All team members must use multi-factor authentication for both SignPath and source code
      repository access").
- [ ] **A released version in the form to be signed.** "The project must already be released in the
      form that should be signed." The first release therefore has to go out *unsigned* — cut it,
      then apply. This is not a workflow limitation, it is the programme's precondition.
- [ ] **An OSI-approved licence with no commercial dual-licensing.** `LICENSE` is MIT, which
      qualifies.
- [ ] **A code of conduct.** There is none in the repository today; the Foundation's own Code of
      Conduct governs the programme, and applicants are normally expected to have one. The
      Contributor Covenant is the usual choice.
- [ ] **Documented functionality on the download page.** `README.md` and the GitHub Releases page
      cover this.
- [ ] **A "Code signing policy" section** on the project home page and on the download/release
      pages. Draft text is at the end of this page — publish it only once the application is
      accepted, because it claims a certificate that does not exist yet.

### 2. Apply

Apply at [signpath.org](https://signpath.org/) (the **Apply** link). The form asks for the
repository URL, the OSI licence, the download/release URL and a short description of the project;
a first-hand account of the process is
[here](https://zenn.dev/shm_7ec/articles/signpath-oss-code-signing?locale=en).

SignPath publishes no turnaround time, and neither does anyone who has written the process up, so
**do not plan a release around a date**. Expect a review, not an automatic approval: "we cannot sign
binaries based on source code that nobody knows. For executable programs that may be downloaded and
executed based on our signature, we require a certain verifiable reputation." A brand-new project
with one release can be turned down.

### 3. Set the project up in SignPath, once accepted

In the SignPath organization you are given:

1. **A project** for this repository. Note its **project slug** and the **organization ID**.
2. **An artifact configuration** describing what CI uploads. The workflow uploads a ZIP holding
   only the files it wants signed, so one configuration serves both rounds:

   ```xml
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <!-- release.yml uploads a directory holding only the files it means to sign: the
          application's .exe and .dll in the first round, the installer in the second. -->
     <zip-file>
       <pe-file-set>
         <include path="*.exe" max-matches="unbounded" />
         <include path="*.dll" min-matches="0" max-matches="unbounded" />
         <for-each>
           <authenticode-sign />
         </for-each>
       </pe-file-set>
     </zip-file>
   </artifact-configuration>
   ```

   The syntax is SignPath's
   ([examples](https://docs.signpath.io/artifact-configuration/examples)). Wildcards rather than
   file names on purpose: the executable is being renamed (#132) and a configuration naming it would
   have to be edited in the SignPath UI on the same day.

   The terms also ask for metadata restrictions — "Set all product name attributes to your project's
   name. Set all product version attributes to the same value in each build." Adding
   `product-name="…"` to `<pe-file-set>` enforces it, but **check first**: the csproj sets no
   `<Product>` or `<Version>`, so the binaries currently carry the assembly name and `1.0.0.0`, not
   the release version. Either set those properties (and pass `-p:Version=` in the publish step) or
   leave the restriction off until you do — a mismatch makes SignPath refuse every request.
3. **A signing policy**, normally `release-signing`. Note its slug. Release signing requires a named
   approver; that is the "Every release needs manual approval for signing" constraint, and the
   workflow waits up to 30 minutes for it.
4. **A CI user** added as a *submitter* on that signing policy, and an **API token** for it.

### 4. Put the secrets in the repository

Settings → Secrets and variables → Actions → *New repository secret*.

| Secret | Where it comes from | Required |
|---|---|---|
| `SIGNPATH_API_TOKEN` | the CI user's API token in SignPath. **The only genuinely secret one.** | yes |
| `SIGNPATH_ORGANIZATION_ID` | the organization's ID (a GUID) in SignPath | yes |
| `SIGNPATH_PROJECT_SLUG` | the project's slug, e.g. `kicad-ultra` | yes |
| `SIGNPATH_SIGNING_POLICY_SLUG` | the signing policy's slug, e.g. `release-signing` | yes |
| `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | the artifact configuration's slug | no — empty selects the project's default configuration |

The last four are identifiers rather than credentials, but they are secrets anyway so that *all* of
them are absent in a fork: the workflow decides whether to sign by asking whether the four required
ones are set, and a fork's release therefore builds unsigned instead of failing against an
organization it cannot reach.

Nothing prints a secret. The gate step names the ones that are *missing* and never reads a value.

### 5. Release, and approve

Push a `v*` tag as usual. The `Pack win` job stops twice, waiting for a signing request, and each
one appears in SignPath for an approver to accept. **Approve both within 30 minutes** or the step
times out and the job fails — nothing is published, so a timed-out release is re-runnable.

A `workflow_dispatch` run signs too, if the secrets are set; that is the only way to rehearse the
wiring without publishing a release, and the requests can simply be denied in SignPath if you did
not mean to spend them.

## Verifying a signed build

The release job already fails if the signature is missing — the step *Check the signed files really
carry a signature* reads the application's two binaries back out of the `.nupkg` that
`releases.win.json` points at, plus `Setup.exe`, and refuses to publish a release whose PE
certificate table is empty. That catches the one failure mode a green signing step does not: a
signing request that succeeds and returns exactly the bytes it was given, because the artifact
configuration did not match.

By hand, on Windows:

```powershell
Get-AuthenticodeSignature .\KiCadUltra-win-Setup.exe | Format-List Status, SignerCertificate
```

`Status` must be `Valid` and the certificate's subject should read `SignPath Foundation`.
`signtool verify /pa /v KiCadUltra-win-Setup.exe` says the same with the chain spelled out.

On Linux or macOS, `osslsigncode verify KiCadUltra-win-Setup.exe` reads the signature without
Windows. To check inside the package, unzip the `.nupkg` and verify `lib/app/*.exe` the same way.

## When the certificate rotates

The certificate lives in SignPath's HSM; this repository never holds it, so a renewal or a
re-issue changes nothing here and needs no release. Three things are worth knowing:

* **Signatures are timestamped**, so binaries already published stay valid after the certificate
  behind them expires. A rotation never invalidates a release that is already out.
* **Velopack does not check signatures on update**, so a rotation cannot break updating either — an
  already-installed copy is not comparing the new binary's certificate against the old one's.
* **The API token is the thing that does expire.** When SignPath issues a new one, replace
  `SIGNPATH_API_TOKEN` and change nothing else. If the token lapses unnoticed, the release fails at
  the signing step rather than shipping unsigned, because the gate only asks whether the secret is
  *set*.

SmartScreen reputation follows the publisher on the certificate — SignPath Foundation — so it
carries across a rotation, and across every other project the Foundation signs.

## If the project ever pays for signing

Velopack's documentation is explicit that "signing needs to be performed by Velopack itself, this is
because the Velopack binaries (such as Update and Setup) need to be signed at different points in
the package build process" ([Code Signing](https://docs.velopack.io/packaging/signing)). That is
true: run `vpk pack` with a recording `--signTemplate` and it asks for the execution stub,
`Squirrel.exe`, the application's files, and then `Setup.exe` — every binary this page lists as
unsigned.

That route is closed to SignPath Foundation, because a signing request has to be submitted by a
GitHub Actions step for an artifact GitHub Actions uploaded — that is how SignPath verifies the
build's origin — and `--signTemplate` runs per file from inside `vpk`. It is open to
[Azure Artifact Signing](https://learn.microsoft.com/en-us/azure/trusted-signing/), which signs a
single file on demand from CI: the change is to delete every SignPath step and add
`--azureTrustedSignFile <metadata.json>` (or `--signTemplate "AzureSignTool sign … {{file}}"`) to the
existing `vpk pack` command.

The catch is eligibility, not money: Public Trust certificates are available to **organizations** in
the US, Canada, the EU, the UK, Australia, New Zealand, Japan, South Korea, Singapore, Switzerland,
Norway and Israel, but **individual developers must be located in the United States or Canada**, and
identity validation takes 1–20 business days
([quickstart](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)). An individual
maintainer outside the US or Canada cannot buy it at all, which is what makes SignPath Foundation
not merely the free option but the only one.

## Code signing policy

*Publish this — as a section of the README or a page it links to, using exactly the words "Code
signing policy" — only once SignPath Foundation has accepted the application. Until then it would
claim a certificate that does not exist. The Foundation requires it on the project home page and on
the download/release pages.*

> ### Code signing policy
>
> Free code signing provided by [SignPath.io](https://signpath.io), certificate by
> [SignPath Foundation](https://signpath.org).
>
> **Roles.** Committers and reviewers: the repository's maintainers. Approvers: the repository
> owner.
>
> **What is signed.** The Windows build of the importer application and its installer, both produced
> by `.github/workflows/release.yml` from the source in this repository. Binaries belonging to
> upstream Open Source projects that are redistributed inside the package — Velopack's updater, the
> .NET runtime, the Chromium Embedded Framework — are not signed by this project.
>
> **Privacy policy.** This program will not transfer any information to other networked systems
> unless specifically requested by the user or the person installing or operating it. It contacts
> component search providers and GitHub's release API only when the user searches for a part or when
> it checks for its own updates; see [Where the parts come from](data-sources.md).

Check that last paragraph against what the application actually does before publishing it.
