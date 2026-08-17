# brisk

brisk scans a Windows PC, explains why it is slow with evidence, fixes findings with one click with full undo, and reclaims disk space through an allowlist-only cleaner. Free, open source, no telemetry, no account, no AI — every diagnosis is a deterministic, readable rule.

## Status

Pre-release. Engine + CLI working; GUI, packaging and docs are coming. Windows 10 1809+ / Windows 11, x64.

Safety model: the cleaner only ever touches an allowlist of caches and temp files that Windows and your apps rebuild on their own. The GUI's one-click clean frees the space immediately (recycle + automatic purge of exactly the items it just recycled — nothing else in your Recycle Bin is touched, and there is no undo for it by design). The advanced per-level cleans and the CLI move items to the Recycle Bin and leave emptying to you. Settings and startup fixes are always undoable.

## Try it

```
dotnet build
```

```
dotnet run --project src/Brisk.Cli -- scan
```

Commands:

```
brisk — Windows performance diagnostics and cleanup

Usage: brisk <command> [options]

Commands:
  scan                       run diagnostics + cleaner scan
    --json                   emit JSON instead of text
  fix                        apply diagnostic rule fixes
    --all                    apply every Auto rule with a finding
    --rule <id>               apply/undo a single rule
    --undo                   undo the named rule's last fix
    --yes                    actually mutate (otherwise dry-run)
  clean                      reclaim disk space
    --level <safe|developer|deep>  which cleanup level to run
    --yes                    actually delete (otherwise print plan)
  targets                    list cleanup targets
  rules                      list diagnostic rules
  version                    print the engine version
```

## License

MIT
