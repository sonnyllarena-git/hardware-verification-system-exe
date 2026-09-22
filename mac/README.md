# macOS build

macOS counterpart to the Windows console tool one directory up. Same flow, same Supabase
submission path, same speed test — only `Services/HardwareDetector.cs` differs, since macOS has
no WMI equivalent and shells out to `sw_vers`/`sysctl`/`diskutil`/`system_profiler` instead.

## Known caveat — not yet verified on real Mac hardware

This was built and compiled on a Windows machine (cross-compiled for `osx-arm64`/`osx-x64`); the
command-line parsing in `HardwareDetector.cs` is based on documented, stable output formats for
those tools, but has not been run against a real Mac. Treat it as a first draft that needs the
same kind of real-hardware validation the Windows build went through (see `../LESSONS.md`'s
history) before trusting it for real applicants — expect to find and fix a few real-machine
surprises on first run, the same way the Windows version did.

## Building a standalone single-file build

```bash
# Apple Silicon (M1/M2/M3/...)
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true

# Intel Macs
dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true
```

Output lands at `bin/Release/net10.0/<rid>/publish/tcp-hardware-check` — a single file, no
`.env` needed (Supabase URL/anon key are hardcoded the same way `tcp-hardware-check-exe` and the
extension's `popup.js` already do it).

Two things a real Mac user will need to do that a Windows user doesn't:
1. **Mark it executable**: `chmod +x tcp-hardware-check` (macOS doesn't have Windows-style
   executable-by-extension; a downloaded file loses its execute bit).
2. **Get past Gatekeeper**: an unsigned, unnotarized binary downloaded from the internet gets
   quarantined by macOS — right-click → Open (not double-click) the first time, and approve it in
   System Settings → Privacy & Security if prompted. This is the direct Mac equivalent of the
   Windows build's own known SmartScreen gap — neither is code-signed yet.
