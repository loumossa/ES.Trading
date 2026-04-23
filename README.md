# ES.Trading

Personal trading workstation for the ES futures session: a NinjaTrader 8 add-on
for live in-session workflow and a separate desktop app for post-session
review and analytics. Both sit on top of a shared core library backed by
SQLite.

## Projects

| Project | TFM | Purpose |
| --- | --- | --- |
| `ES.Trading.Core` | `net48` + `net8.0` | Shared domain, persistence (Dapper + SQLite), market overview sources. Multi-targeted so the NT8 add-on (net48) and desktop app (net8.0) can both reference the same source. |
| `ES.Trading.NTAddon` | `net48` | NinjaTrader 8 add-on. WPF panel with opening-range calc, alerts, and execution listener. Outputs directly into `Documents\NinjaTrader 8\bin\Custom\`. |
| `ES.Trading.DesktopApp` | `net8.0-windows` | WPF app for trade log review, discipline scoring, macro levels, and market overview. Charts via LiveCharts2. |

## Build

Requires Visual Studio 2022 (or `dotnet` 8 SDK) and .NET Framework 4.8 dev
pack. NinjaTrader 8 must be installed for the add-on project to resolve
`NinjaTrader.Core.dll` / `NinjaTrader.Gui.dll` from its install directory.

```
dotnet build ES.Trading.sln
```

The desktop app runs standalone. The NT8 add-on needs a one-time stub copy
into NT8's `AddOns\` folder and an in-NT8 reference add — see
[`deploy/README.md`](deploy/README.md) for the full setup and rebuild flow.

## Data

SQLite database lives under the user profile; repositories in
`ES.Trading.Core` own the schema. Both apps point at the same database so
trades captured live by the NT8 add-on are available in the desktop app for
review.
