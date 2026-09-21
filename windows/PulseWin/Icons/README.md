# Provider marks

These five SVG files are **copied byte-for-byte** from the fork's own
`Sources/Pulse/Resources/` at the repository root. They are not redrawn, retraced
or re-exported.

| File | Provider | Upstream `Provider` case |
|---|---|---|
| `openai.svg` | Codex | `.codex` (icon resource `openai`) |
| `opencode.svg` | OpenCode Go | `.openCodeGo` |
| `qingyan.svg` | Zhipu | `.glmCoding` (icon resource `qingyan`) |
| `zai.svg` | z.ai | `.zai` |
| `deepseek.svg` | DeepSeek | `.deepSeek` |

## Provenance and licence

They come from [Lobe Icons](https://github.com/lobehub/lobe-icons), which is
distributed under the MIT licence. The repository's `THIRD_PARTY_NOTICES.md`
already carries that notice and names these exact files, so nothing new is being
introduced by copying them here — this file only records *why* there is a second
copy inside `windows/`.

The copy exists so that `windows/` builds on its own. Referencing the files under
`Sources/Pulse/Resources` through MSBuild would avoid the duplication, at the cost
of a `windows/` folder that cannot be built without the rest of the repository —
and the whole point of the port is that it is a separate, self-contained
application.

The names and marks remain the property of their respective owners. Their inclusion
identifies compatible services and does not imply endorsement.

## Why a copy rather than a conversion

`Ui/ProviderIcons.cs` parses the `d` attribute straight into a WPF `Geometry` at
run time. The two path mini-languages are close enough that this is a parse rather
than a conversion, which is why there is no SVG library and no build-time
conversion step here: the files stay the files.

All five are `fill="currentColor"` on a `viewBox="0 0 24 24"` grid, so they are
**monochrome templates** and are drawn as such — in the ring's foreground colour,
never in a brand tint. On a ring, colour means usage.
