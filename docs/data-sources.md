# Where the parts come from

Every result names the source its data came from, because these are other people's services and one
of them is not a published API. The app never invents a field: a value appears because a source
returned it, and a source that cannot answer is left out of the results rather than shown empty.

| Source | What it gives | What you need |
|---|---|---|
| **EasyEDA / LCSC** | JLCPCB's parts library: LCSC code, manufacturer, stock, price breaks, datasheet | nothing |
| **Octopart** | Multi-distributor stock and pricing, datasheets, real CAD availability | your own [Nexar](https://nexar.com/) token, in Settings |
| **[Ultra Librarian](https://www.ultralibrarian.com/)** | Symbols, footprints and 3D models, through the browser tab | a free account, entered in that browser |
| **SnapEDA** | browser only | no API a desktop application may use ([#56](https://github.com/danielmeza/kicad-ultra/issues/56)) |
| **Component Search Engine (SamacSys)** | browser only | its terms forbid automated search ([#57](https://github.com/danielmeza/kicad-ultra/issues/57)) |

## EasyEDA / LCSC: two routes

**JLCPCB's official Components API**, when you have entered your own credentials in **Settings →
Component Providers**. JLCPCB reviews applications for API access per service type, and this needs
the **Parts** permission approved; see
[its guide](https://jlcpcb.com/help/article/jlcpcb-online-api-available-now). Credentials that are
refused — not approved, or invalid — are reported once, and the app takes the other route for the
rest of the session rather than losing your search. Editing any of the three values starts over.

That API looks parts up **by LCSC number only** (`C2040`, …). It has no keyword search, so keyword
searches take the second route even with credentials.

**An internal endpoint of JLCPCB's website**, otherwise. It is not a published API, so it can change
or stop working at any time without notice, and the Part Explorer says so while it is in use. One
page of 25 results per search, an honest `kicad-ultra/1.0` User-Agent, and a rate limit of its own —
one request at a time, three seconds apart — which is below what the endpoint was measured to
tolerate. When JLCPCB turns searches down anyway, the app says "JLCPCB is rate-limiting; try again
shortly", backs off for up to two minutes, and keeps serving cached results meanwhile.

**The MCP server never uses the official API**, even with credentials stored: JLCPCB's API terms
forbid passing API data to third parties, and an MCP server hands every result to the connected AI
client. It is not a setting — the server refuses to start in a configuration that would allow it.

## CAD availability

`Sym`, `FP` and `3D` on a result are three-state: **yes**, **no**, or **unknown** with a `?`. Unknown
is not a polite "no": it means the source did not say. Octopart reports real availability; JLCPCB's
data carries none, so EasyEDA / LCSC rows are unknown until an import proves otherwise, after which
the row marks what that import actually produced.

## Trademarks and attribution

UltraLibrarian, EasyEDA, LCSC, JLCPCB, Octopart, Nexar, SnapEDA and SamacSys are trademarks of their
owners, named here and in the app only to identify the services and data this plugin works with. This
project is not affiliated with or endorsed by any of them.

JLCPCB's API terms forbid its trademark or logo in a partner's advertising, and "JLC" in a partner's
website URLs, on pain of losing API access. So this repository carries no JLCPCB or LCSC logo, the
plugin's name, icon and Plugin and Content Manager listing do not mention them, and no URL this
project controls contains "JLC"
([#58](https://github.com/danielmeza/kicad-ultra/issues/58)).

easyeda2kicad is © its authors under AGPL-3.0. It is a separate program you install, not a part or a
dependency of this MIT-licensed project, and it fetches part data from EasyEDA itself: that traffic
comes from the tool you installed, not from kicad-ultra.
