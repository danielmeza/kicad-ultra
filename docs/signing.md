# Code signing

The importer ships as a self-contained Windows build that a user downloads and runs, and that then
replaces its own binaries through Velopack. Unsigned, Windows tells that user the app is
unrecognized and its publisher unknown, and no release ever earns a reputation that the next one
can inherit.

This page is the technical half: what an unsigned build means for a user, what the release workflow
signs and what it deliberately does not, which programme was chosen and why, and how to verify a
signed build. The maintainer's half — the application itself, field by field, in the order it has to
happen — is [Applying to SignPath Foundation](signpath-application.md).

**Nothing here is done yet, and that is the plan for now.** No certificate exists, no application
has been made, and no secret is set. The workflow is wired and every signing step skips when the
secrets are absent, so the releases cut in the near future are complete, unsigned releases that
build, publish and install exactly as they do today. Read the next section before deciding how
urgent a certificate is.

## What an unsigned build means for a user

**The installer.** Someone who downloads `KiCadUltra-win-Setup.exe` from a GitHub release gets it
with the Mark of the Web, and running it shows *Windows protected your PC — Microsoft Defender
SmartScreen prevented an unrecognized app from starting*. There is no Run button on that dialog:
they have to click **More info** and then **Run anyway**, and the details read *Publisher: Unknown
publisher*. Microsoft's own table for "No signature" says exactly that, and adds "Enterprise policy
can prevent continuation entirely"
([SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation))
— on a managed machine the warning is a refusal with nothing to click through.

**The portable zip**, extracted with Explorer, behaves the same way: Explorer copies the Mark of the
Web onto the extracted files.

**The plugin's own path mostly escapes that, but not entirely.** `importer_launcher.py` downloads
the portable zip with Python and unpacks it with `zipfile`; neither writes a Mark of the Web, so the
SmartScreen download prompt does not appear for the copy the plugin runs. Two things are not
MOTW-gated, though:

* **Smart App Control** on Windows 11 — "Smart App Control will block execution of unsigned files
  unless the file has a positive reputation. Smart App Control signature checks apply to all
  executable files, not just those downloaded from the Internet"
  ([SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)).
  On a machine where it is on, the unsigned importer can simply refuse to start, with no dialog to
  accept.
* **Antivirus heuristics.** A 200 MB unsigned binary that unpacks itself into `%LOCALAPPDATA%` and
  then rewrites its own files is a shape scanners dislike, and a signature is the cheapest signal
  that it is not what it looks like.

**Reputation never accumulates while nothing is signed.** "When a file is not signed, SmartScreen
reputation must build for each new version of your files, starting with zero reputation. Reputation
cannot transfer from previous versions unless both were signed using the same publisher identity."
Every release starts from zero, for ever. Signing does not make the first-download warning go away
— a new certificate has no reputation either, and EV stopped bypassing SmartScreen in 2024 — but it
is the only thing that lets one release's reputation carry to the next.

**Updating is unaffected, signed or not.** Velopack does not check Authenticode on an update: it
fetches the `.nupkg` over HTTPS from GitHub Releases, checks it against the SHA-256 in
`releases.win.json`, unpacks it and swaps `current/`. Nothing fails, nothing warns, and the user is
not prompted — a self-update of an unsigned build works exactly like a self-update of a signed one.
Two consequences worth being clear about:

* Because the updater writes the new files itself, they carry no Mark of the Web, so a user who
  clicked through the warning once does not meet it again on later versions. SmartScreen is a
  first-install problem here, not a per-update one. Smart App Control is the exception, since it
  does not care where a file came from.
* Signing would **not** add a check to the update path, because nothing in that path verifies
  signatures. What protects an update is HTTPS to GitHub plus that SHA-256, and `SHA256SUMS.txt`
  for the plugin's first download. Signing is about what Windows does when a *user* launches a
  file, not about what the updater accepts.

## What the release workflow does

`.github/workflows/release.yml` signs the Windows artifacts through
[SignPath Foundation](https://signpath.org/), in two rounds, and only when the four `SIGNPATH_*`
secrets exist:

| File | Signed | When |
|---|---|---|
| `KiCadUltra.exe` (the apphost a user runs) | yes | before `vpk pack` |
| `KiCadUltra.dll` (the application itself) | yes | before `vpk pack` |
| `KiCadUltra-win-Setup.exe` (the installer) | yes | after `vpk pack` |
| `Squirrel.exe` / `Update.exe` (Velopack's updater) | **no** | — |
| `KiCad Ultra.exe` in the portable archive, `KiCad Ultra_ExecutionStub.exe` in the package (the same stub) | **no** | — |
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
double-clicks the `KiCad Ultra.exe` at its root runs Velopack's unsigned stub. (That file is named
from `--packTitle`, not from `--packId`, which is why it has a space in it; checked by packing a
throwaway application with vpk 1.2.158.) The plugin's own path does not go near it — the launcher
runs `current/KiCadUltra.exe` inside the extracted archive directly, and that file is signed.
Closing the gap needs a signing route that `vpk` can call per file; see
[If the project ever pays for signing](#if-the-project-ever-pays-for-signing).

It also leaves the two unsigned Velopack binaries exposed to **Smart App Control**, which does not
care about the Mark of the Web and blocks unsigned executables outright on the Windows 11 machines
where it is enabled. Signing the application does not help `Update.exe`; only `vpk` doing the
signing would.

And to repeat the two limits from [the first section](#what-an-unsigned-build-means-for-a-user),
because they do not go away once a certificate exists: signing adds **no** check to the update path,
which is protected by HTTPS and SHA-256 and nothing else, and it buys **no** instant SmartScreen
clearance — reputation builds over time for OV certificates and Azure Artifact Signing alike, and
EV stopped bypassing SmartScreen in 2024
([Code signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)).
What signing buys is that reputation carries from one release to the next instead of restarting.

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

Two of the others can be ruled out plainly rather than left as options:

* **Certum's Open Source certificate cannot sign from CI at all.** It arrives as a cryptographic
  card with a reader, by courier; the private key is on that card and a GitHub-hosted runner cannot
  reach it. Using it would mean a self-hosted runner with the card plugged into it, or signing
  every release by hand on a desk. For €69 that is not a bargain, it is a different workflow.
* **Azure Artifact Signing may simply not be purchasable**, which is a question only the maintainer
  can answer: Public Trust certificates are open to *organizations* in the US, Canada, the EU, the
  UK, Australia, New Zealand, Japan, South Korea, Singapore, Switzerland, Norway and Israel, but
  "Individual developers must be located in the United States or Canada"
  ([quickstart](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)).
  **If you are eligible, it is worth the ~$10/month even though SignPath is free**, and not for the
  cost: it is the only route `vpk` can drive itself, so it signs the updater and the portable stub
  as well — see [If the project ever pays for signing](#if-the-project-ever-pays-for-signing). If
  you are not eligible, SignPath Foundation is not merely the free option, it is the only one.

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

### 1. Everything up to a certificate

Meeting the conditions, cutting the release the programme insists comes first, publishing the code
signing policy, filling in the form, and creating the SignPath project, artifact configuration,
signing policy and CI user: all of it is written out step by step, with every answer already
drafted, in [Applying to SignPath Foundation](signpath-application.md). Work through that page, then
come back here for the secrets.

Four things from it belong here too, because they are what this workflow depends on:

- **An unsigned release comes first.** "The project must already be released in the form that should
  be signed", so v0.1.0 ships unsigned and the application follows it. That is the programme's
  precondition, not a limitation of the workflow.
- **One artifact configuration serves both rounds**, because each round uploads a ZIP holding only
  the files it means to sign. It matches by wildcard rather than by file name, and restricts
  `product-name`, `product-version` and `company-name` — the metadata restrictions the terms
  require ("All signed binaries must have metadata attributes set and enforced using file metadata
  restrictions"). The XML is in the packet.
- **The workflow passes the version with each signing request**, as the `version` parameter that
  configuration declares. The two go together: a request carrying a parameter the configuration does
  not declare fails, and so does a configuration whose restriction nothing satisfies. What makes the
  restriction satisfiable is that the application's binaries and Velopack's installer now agree on
  their product name and version — see the measured table in the packet.
- **A named approver, or nothing is ever signed.** "Every release needs manual approval for
  signing."

Expect a review rather than an automatic approval: "we cannot sign binaries based on source code
that nobody knows. For executable programs that may be downloaded and executed based on our
signature, we require a certain verifiable reputation." There is no published turnaround time, so
**do not plan a release around a date**.

### 2. Put the secrets in the repository

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

### 3. Release, and approve

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

The catch is eligibility rather than money, and it is the one question worth answering before
applying to SignPath at all: identity validation takes 1–20 business days, and **individual
developers must be located in the United States or Canada**
([quickstart](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)). If that is you,
~$10/month buys a route with no manual approval on every release, no "the project must already be
released in the form that should be signed" precondition, your own name on the certificate instead
of the Foundation's, and — because `vpk` does the signing — the updater and the portable stub signed
too. If it is not, the question does not arise.

## Code signing policy

The terms require a section headed exactly **"Code signing policy"** on the project's home page and
on its download/release pages. The text to publish — in two variants, one for the application and
one for after the certificate is issued — is in
[Applying to SignPath Foundation](signpath-application.md#step-3--publish-the-code-signing-policy),
with the roles, what is and is not signed, and a privacy paragraph.

Two things about it were wrong in the first draft of this page and are worth stating plainly, since
it is easy to repeat them:

- **It is published *with* the application, not after acceptance.** The draft said to wait, on the
  reasoning that publishing earlier would claim a certificate that does not exist. But the
  application form asks for a Download URL and says that page "must provide signing information
  according to SignPath Foundation Terms of Use" — the policy is part of what gets reviewed. The
  variant written for that moment says on its face that the certificate is applied for and not yet
  granted, and one clause is deleted when it is.
- **The Foundation's suggested privacy sentence would be untrue here.** "This program will not
  transfer any information to other networked systems unless specifically requested by the user"
  does not describe this application: `AppUpdateService` asks GitHub for a newer release 20 seconds
  after startup and every six hours after that, without anyone requesting it. The terms accept a
  statement *or* a link to a privacy policy, so the packet's variant says what the program actually
  does.
