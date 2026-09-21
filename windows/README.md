# PulseWin — a Windows port of Pulse

A tray-resident, edge-docked monitor for AI coding allowances, in C# / .NET 8 + WPF.

This directory is **not** a patch to the Swift app. SwiftUI, AppKit, `NSPanel` and
`NSStatusItem` do not exist on Windows, so the interface layer could not be
modified into a Windows one — it had to be written again. What *was* carried over,
deliberately and carefully, is everything behind the interface: the routes each
service is actually reached by, the shapes their replies come back in, the
credential stores they borrow from, and the rules about what may and may not be
drawn.

That distinction is the whole of this README, so it is worth stating as numbers.

## What was carried over, and what was written again

Measured against the upstream sources at the time of the port:

| Module | Files | Lines | Apple-UI dependent | Portable logic |
|---|---:|---:|---:|---:|
| `Sources/Pulse/Usage` | 88 | 20,069 | 982 | 19,087 |
| `Sources/Pulse/Panel` | 29 | 9,101 | 7,885 | 1,216 |
| `Sources/Pulse/Providers` | 28 | 8,387 | **0** | 8,387 |
| `Sources/Pulse/Settings` | 14 | 5,778 | 5,693 | 85 |
| `Sources/Pulse/Auth` | 10 | 2,968 | 1,405 | 1,563 |
| `Sources/Pulse/App` | 12 | 2,946 | 2,395 | 551 |
| **total** | **245** | **49,249** | **18,360 (37%)** | **30,889 (63%)** |

The `Providers` directory imports no UI framework at all — it is `URLSession` and
`JSONDecoder` from end to end — which is why the route knowledge could be moved
rather than reverse-engineered. There is also **no `Security` / Keychain import
anywhere** in the project: Pulse encrypts its own key store with CryptoKit, so the
Windows side needed a storage swap (`DPAPI`) rather than a Keychain port.

The non-UI Apple-only dependencies were few and each has a direct replacement:
`IOKit` ×2, `Network` ×2 (proxy discovery) → `SocketsHttpHandler`,
`os` ×2 (logging), `UserNotifications` ×1 → tray balloon,
`ServiceManagement` ×1 → the registry Run key, `Sparkle` ×1 → dropped.

## What is ported faithfully

These are the rules that make the upstream app trustworthy, and each one has a
test or a comment defending it here.

**A percentage always comes from the provider.** Nothing derives one from local
token counts. Where a service reports no fraction, the interface says so.

**A figure that is absent is absent, not zero.** DeepSeek sends every number as a
string; a field that is missing or unparseable stays null. A balance read as zero
is a full red ring and a notification announcing a spent account.

**"Spent" is the provider's verdict, not arithmetic.** Codex's `limit_reached`,
DeepSeek's `is_available`. A spend limit may sail past 100%.

**A group's "limit reached" belongs to the window that caused it.** Codex flags a
whole group; only the fullest window in it is marked, so the claim stays as precise
as the data allows.

**A window's kind comes from its duration, never from which slot it arrived in.**
ChatGPT Pro has no 5-hour limit and reports its weekly one as `primary_window`.

**A length that is only a sort key may not be divided by.** `ReportsLength` gates
the elapsed-window arc and the forecast, so OpenCode Go's nominal weekly/monthly
lengths never draw a clock nobody reported.

**A refusal inside an HTTP 200 is still a refusal.** Zhipu's quota endpoint answers
`200 OK` with `success: false`, in Chinese on the mainland host, and a working key
on an unsubscribed account answers `500` with a sentence — not a code — saying so.
Both are read from the envelope's own text, and an outage is not reported as a bad
key.

**Prepaid credit is not a limit.** DeepSeek's row is `balance`, carries no reset
and no length, and its denominator comes from exactly three places: the highest
balance this app has watched, nothing at all, or a figure the reader typed. The
"since top-up" mode is *measured, not inferred* — a balance that rises can only be
a top-up.

**A figure never rounds away the fact that there is *some*, or that there is *not
all*.** Both ends are held off the extremes: nothing left reads 0%, anything left
reads at least 1%, nothing used reads 100%, and anything used reads at most 99%. The
two views need not sum to 100, because only one is ever on screen. A non-finite
fraction reads 0% rather than trapping — upstream crashed on every launch until an
`inf` budget was cleared from Settings, because the figure had been persisted.

## Parser verification

`PulseWin.exe --fixtures` runs **50 assertions** against captured service replies
and needs no network, no key and no account. The Zhipu and DeepSeek bodies are the
ones upstream captured from the live services and committed to its own test
fixtures, so they are real traffic; the Codex bodies are **constructed** from the
field names its service reads, because no captured body was published — they verify
that this port reads that shape consistently, not that the shape is what the live
endpoint still sends. That limitation is labelled in the source too.

```
50 passed, 0 failed
```

`PulseWin.exe --selftest` adds credential discovery, a live fetch of every
provider, and the DeepSeek baseline marks.

## Coverage

Five of upstream's nineteen providers:

| Provider | Route | Credential |
|---|---|---|
| **Codex** | `GET chatgpt.com/backend-api/wham/usage` | borrows `%USERPROFILE%\.codex\auth.json` |
| **OpenCode Go** | `GET opencode.ai/zen/go/v1/usage` | a pasted key, else OpenCode's own `auth.json` |
| **Zhipu** | `GET open.bigmodel.cn/api/monitor/usage/quota/limit` | a pasted key, else a saved GLM key file |
| **z.ai** | same path on `api.z.ai` | a pasted key |
| **DeepSeek** | `GET api.deepseek.com/user/balance` | a pasted key |

Multi-account is supported for **Codex**, which is the one that needs it: an added
account carries its own token and is sent as `ChatGPT-Account-Id`, because an empty
account header lets the service answer for whichever login it likes — which would
put one subscription's figures under another's ring.

### Not ported

- The other fourteen providers. The pattern for adding one is in upstream's
  `Docs/providers/README.md` and is followed here: a service returning
  `ProviderUsage`, plus metadata in `Core/Provider.cs`.
- `codex app-server`. Upstream falls back to spawning the CLI and speaking
  JSON-RPC to it when the stored token has aged out, so that a dead token is
  recovered rather than reported. Here it is reported, and running `codex` once
  clears it. This is the largest functional gap and it is stated in the UI.
- Token-spend history, notifications, the animated marks, Liquid Glass, the
  Sparkle updater, and the browser-session providers.

## Differences that are Windows-specific, and why

**The rail counts down by default.** This is the one place this port deliberately
disagrees with upstream, and it is worth stating plainly. Upstream's
`showsRemaining` defaults to `false`, so its ring and figure count *up* — what is
gone — and the word that removes the ambiguity ("88% Used" / "12% Left") lives in
the hover card. That is fine on a card, but the rail carries a bare percentage, and
a small number under a nearly empty ring reads as "almost nothing left" whichever
way it was counted. Counting down makes the picture a fuel gauge: a full ring means
a full tank, and a full ring cannot be misread.

The **colour still comes off what is gone** in both modes, which is upstream's rule
and the right one — how close a limit is does not change because the figure beside
it was counted from the other end, so a sliver of quota left stays a small red arc
rather than a large green one. An exhausted window is a full ring either way.

Settings carries the toggle, and the hover card now names the direction as well as
carrying upstream's `Used` / `Left` suffix on every row.

**Credential storage is DPAPI**, not a hand-rolled encrypted file. CryptoKit plus
owner-only permissions is the right answer on macOS; on Windows,
`ProtectedData` at `CurrentUser` scope ties the ciphertext to the account, so
copying `keys.dat` elsewhere yields nothing.

**File discovery is plural.** Upstream has one path for OpenCode's `auth.json`,
because macOS has one convention. Windows does not — OpenCode follows an
XDG-style layout on some installs and an `%APPDATA%` layout on others — so all the
plausible homes are listed and the first that exists wins.

**The rail docks against `SystemParameters.WorkArea`**, which is already in
device-independent units and already excludes the taskbar. A raw monitor rectangle
would have to be divided by a DPI scale and corrected by hand, differently on every
monitor.

**The rail is a `WS_EX_TOOLWINDOW`.** `ShowInTaskbar = false` keeps it off the
taskbar but not out of Alt-Tab, and an always-on-top strip in the window switcher
is a window, not a monitor.

### Adding a Codex account

There is **no field to paste a token into**, and that is the design rather than an
omission. Settings › Codex accounts › **Sign in to another account** runs OpenAI's
device-code flow: it shows a short code, opens `auth.openai.com/codex/device`, and
polls until the sign-in is approved.

Pasting a copied token is what this first did, and it does not work. A Codex access
token lasts on the order of 240 hours, so a pasted one dies in about ten days — and
the only thing that can renew it is the refresh token the Codex CLI is also relying
on, which means trying to fix the ring signs you out of your own Codex. Upstream
measured this and moved to a real sign-in for the same reason. A signed-in account
here has its own refresh token and touches nothing that belongs to the CLI.

The flow is **OpenAI's device-code shape, which is not RFC 8628's**. They share a
name and almost nothing else:

| | This one | RFC 8628 |
|---|---|---|
| Request bodies | JSON | form |
| "Still waiting" | **403 and 404** | `authorization_pending` in a 400 |
| Proof key | the provider generates it | the client generates it |

Both halves of that were verified against the live service before the code was
written, not inferred: `POST /api/accounts/deviceauth/usercode` answers `200` with
`device_auth_id`, `user_code` and — note — **`"interval": "5"` as a string**, and
polling an unapproved code answers `403 Device authorization is pending.` The reply
also carries `expires_at`, which is used rather than a fixed fifteen-minute guess;
a guess would keep polling a code the service has already discarded and then report
the failure as the reader's.

Two smaller things it inherits from upstream's notes: the exchange sends **four
fields and not `state`** (Anthropic's takes a state, this one does not), and the
form body is percent-encoded strictly — a general-purpose encoder leaves `:` and
`/` alone and passes `+` through as a space, which is a different string than the
one that was signed.

Removing an account removes its login with it. Leaving the refresh token behind
would keep a credential for an account the reader has just taken off the rail, and
re-adding would silently inherit it.

### Moving the rail

Drag it anywhere with the left button. Let go:

- **near an edge** → it docks there, flush, and the position along that edge is
  remembered;
- **in open desktop** → it stays exactly where it was put, and stays there across
  restarts.

The snap distance is 160 px and deliberately generous, because a drop clearly aimed
at an edge that lands a few pixels short reads as the snapping being broken rather
than as the drop having missed. Double-click — or the right-click menu's *Put the
rail back on its edge* — returns it to a centred dock, which is also the only way
back from a rail dragged half off the screen.

**The docked side is squared off.** Rounding all four corners and sitting the slab
flush leaves two small crescents of desktop showing through at the corners, which
undoes the flush position: the slab still reads as floating. Verified by sampling
the corner pixel — `(29,29,31)` where a rounded corner would show the desktop's
`(255,255,255)`.

## The provider marks

Each ring carries the product's own mark, taken from the fork's
`Sources/Pulse/Resources` — the same [Lobe Icons](https://github.com/lobehub/lobe-icons)
files upstream bundles, copied byte-for-byte and parsed straight into WPF geometry
at run time. `Icons/README.md` records the provenance; the MIT notice is already in
the repository's `THIRD_PARTY_NOTICES.md`.

**They are monochrome templates and are drawn as such.** Every file is
`fill="currentColor"` on a `viewBox="0 0 24 24"` grid, which is what that attribute
means: the mark has no colour of its own and takes the foreground it is given. That
is upstream's rule and it is the right one — on this surface **colour means usage**,
so the only coloured thing on a ring is the arc. A brand tint would put two meanings
on one surface and make a green mark beside a green arc say nothing.

Two details that took checking rather than guessing:

- **The path data goes straight through.** The two path mini-languages are close
  enough that this is a parse, not a conversion, so there is no SVG library and no
  build-time conversion step — the files stay the files. The risk was the packed arc
  flags SVG allows (`a5.526 5.526 0 01-…`, where `01` is *largeArc=0, sweep=1* with
  no separator); `--selftest` now reports every mark's parse status and bounds, and
  all five go through unchanged.
- **Scaled from the 24×24 grid, not fitted to the ink.** The bounds differ — 16×20
  for OpenCode, 24×23.8 for OpenAI — because they are drawn that way on purpose,
  each sized to its own optical weight. Fitting every mark to the same box would
  flatten exactly the difference the designers put there.

The letters survive only as the fallback for a mark that will not parse, which is a
missing mark rather than a failure: the ring still carries the figure.

## Localisation

English and Simplified Chinese, chosen at the top of Settings. The default is
**Follow Windows**, which reads `CurrentUICulture` — so a Chinese Windows opens in
Chinese without anybody finding the row, and any other culture gets English rather
than an empty interface.

The strings live in `Localization/` as an **abstract class with one implementation
per language**, not a keyed lookup. That is the whole point: a dictionary falls back
silently when a key is missing or misspelled, so the symptom of an incomplete
translation is one stray English word in a corner nobody looks at. With abstract
members the compiler refuses to build a language that has not answered every
question — adding a string to the interface is a build error until both languages
have it.

**Product names are not translated** — Codex, OpenCode Go, Zhipu, z.ai, DeepSeek
are what those products are called in every language, which is upstream's rule for
its `displayName` too. A window's `Scope` is a model name and is likewise left
alone.

Two things follow the language rather than a preference:

- **Money grouping.** English groups by thousands, so a hundred thousand reads
  `100k`; Chinese groups by ten thousands, so the same figure reads `10万`. Verified
  by `--selftest`, which prints both: `¥12.3k / ¥250M` against `¥1.2万 / ¥2.5亿`.
- **The CJK font.** The face stack names `Microsoft YaHei UI` explicitly rather than
  trusting WPF's per-run fallback, because this app draws text both through
  `TextBlock` and straight into `FormattedText`, and fallback is resolved separately
  for each.

A missing CJK face does not throw — WPF draws a hollow "tofu" box that looks like a
rendered character until you read it. So `--selftest` renders one and checks for ink
in the **middle** of the glyph box: a tofu box is hollow, and 中 is not. It reports
`OK — 225 ink pixels inside the box, so a real glyph was drawn`.

The `--selftest` and `--fixtures` reports are deliberately left in English. They are
a diagnostic for whoever is fixing a provider, not part of the interface.

## Size

Measured on this build:

| Publish | Size | Files | Notes |
|---|---:|---:|---|
| Framework-dependent | **0.3 MB** | 5 | needs the .NET 8 desktop runtime |
| Self-contained, single file | **62.9 MB** | 2 | runs on a machine with nothing installed |

The app's own code is only ~307 KB of that (a 159 KB `PulseWin.dll` and a 148 KB
apphost); the 62.9 MB is the .NET runtime and WPF bundled whole. Source is 31 files
and about 5,300 lines.

## Build and run

Needs the **.NET 8 SDK**. No administrator rights:

```powershell
# per-user SDK, if you do not already have one
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1
& $env:TEMP\dotnet-install.ps1 -Channel 8.0 -InstallDir "$env:USERPROFILE\.dotnet"
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
```

Then:

```powershell
cd windows\PulseWin
dotnet build
dotnet run -- --fixtures     # 42 parser assertions, no key needed
dotnet run -- --selftest     # credential discovery + a live fetch of each service
dotnet run                   # tray icon and rail
```

A self-contained single-file build, for handing to someone without the runtime:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The settings file and the encrypted key store live in
`%APPDATA%\PulseWin\`. The DeepSeek baseline is `deepseek-baseline.json` in the
same folder, one mark per currency.

## Layout

```
Core/          Provider metadata, UsageWindow, ProviderUsage, HTTP, JSON helpers
Providers/     One service per route — the ported part
Storage/       DPAPI credential store, settings, the DeepSeek baseline
Services/      UsageStore: the refresh loop and the state behind the rail
Ui/            RailWindow, RailRow, RingControl, DetailCard, SettingsWindow, TrayIcon
Ui/ProviderIcons  The provider marks, parsed from SVG into WPF geometry
Localization/  Strings, with one implementation per language
Icons/         The five SVG marks, copied from Sources/Pulse/Resources
FixtureCheck   The 50 assertions against captured replies
SelfTest       Credential discovery, a live fetch of every provider, the CJK check
```

The interface is built in code rather than from XAML. A tray-resident borderless
overlay has no document to describe — every window here is positioned by arithmetic
against a monitor's work area, not by a layout system — so XAML would add a compile
step and a file to keep in sync without carrying any of the decisions.

## Attribution

Pulse is by [@qunqin24](https://github.com/qunqin24), Apache 2.0, and inspired a UI
concept shared by [Vinz (@hivinz_)](https://x.com/hivinz_) in August 2026. This port
inherits the licence and keeps the upstream `LICENSE` and `THIRD_PARTY_NOTICES.md`
intact at the repository root. The changes here are a port, not a fork of the
product: the provider routes, the interpretation of each reply, and the rules about
what may be drawn are upstream's work, and the comments in `Core/` and `Providers/`
say so where a rule came from a specific upstream bug.
