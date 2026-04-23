# NT8 Deployment

NinjaTrader 8 only discovers `AddOnBase` subclasses from its own
`NinjaTrader.Custom.dll` — external DLLs dropped in `bin\Custom\AddOns\`
are invisible to add-on discovery. So the deploy has two parts:

1. **The DLL** — `ES.Trading.NTAddon.dll` (built from this repo). Lives in
   `Documents\NinjaTrader 8\bin\Custom\`. The csproj outputs here directly.
2. **The stub** — `NinjaScriptStub\ESTradingAddon.cs` (this folder). Must be
   copied into `Documents\NinjaTrader 8\bin\Custom\AddOns\`. NT8 compiles
   it into `NinjaTrader.Custom.dll`; that's where the `AddOnBase` subclass
   lives so NT8 finds it. The stub delegates all real work to the DLL.

## One-time setup

1. Build the solution in Visual Studio / `dotnet build`.
   - Verify `ES.Trading.NTAddon.dll` (and its deps — `ES.Trading.Core.dll`,
     `Dapper.dll`, `Microsoft.Data.Sqlite.dll`, etc.) are in
     `Documents\NinjaTrader 8\bin\Custom\`.
2. Copy `deploy\NinjaScriptStub\ESTradingAddon.cs` to
   `Documents\NinjaTrader 8\bin\Custom\AddOns\`.
3. Open NT8. Tools → Edit NinjaScript → AddOn → ESTradingAddon.
4. In NinjaScript Editor: right-click the project → References → add
   `ES.Trading.NTAddon.dll` from `bin\Custom\`. Add `ES.Trading.Core.dll`
   too if the compiler complains.
5. Press **F5** to compile. Fix any errors.
6. Restart NT8 (full close + relaunch).
7. Tools menu → **ES Trading Panel** should now be available.

## Rebuild flow

After the one-time setup, normal dev cycle is:

1. `dotnet build` (outputs refresh in `bin\Custom\`).
2. Restart NT8. (NT8 caches loaded assemblies; restart is required to pick
   up a new DLL. No recompile of the stub needed unless its signature
   against the host changes.)

## Troubleshooting

- **"ES Trading Panel" not in Tools menu**: check `Documents\NinjaTrader 8\log\`
  for errors mentioning `ES.Trading` or `ESTradingAddon`. Usual cause: a
  dependency DLL is missing from `bin\Custom\`, or the stub's reference to
  the main DLL wasn't added in NinjaScript Editor.
- **F5 compile errors on the stub**: the DLL reference wasn't added, or the
  host class signature changed. Re-add the reference.
- **Add-on loads but window doesn't appear**: check NT8 Control Center →
  Log tab for exceptions from `ESTradingAddonHost.Initialize`.
