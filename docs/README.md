# Documentation

| Page | What's in it |
|---|---|
| [Installing](installing.md) | Building and staging the app, where KiCad looks for plugins, enabling the API, easyeda2kicad, and where settings, logs and credentials live |
| [Where the parts come from](data-sources.md) | Each source and what it needs, JLCPCB's two routes, CAD availability, and the attribution and trademark rules |
| [Troubleshooting](troubleshooting.md) | A disabled Import button, a part that does not appear, what the About window is telling you, rate limiting, failed imports, and where the logs are |
| [Building and testing](building.md) | The build gates, running the app during development, and building against local checkouts of SExpressions and KiCadSharp |
| [Releasing](releasing.md) | What this repository publishes, and where the .NET libraries went |
| [Code signing](signing.md) | What an unsigned Windows build means for a user, what the release signs and what it does not, why SignPath Foundation, and how to verify a signed build |

[CLAUDE.md](../CLAUDE.md) at the repository root is the deeper guide: the architecture, every build
constraint, and the traps that have already cost someone a day. It is kept current as the code
changes, so it is the first thing to read before touching the code.
