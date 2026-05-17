# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project does not yet follow Semantic Versioning — entries are
grouped under dated headings and the `Unreleased` section.

## [Unreleased]

### Fixed
- **NT8 AddOn no longer fails to capture executions.** The hard-coded
  `ES 06-25` contract in the NinjaScript stub's `BarsRequest` was expired
  and threw during `State.SetDefaults`, preventing `State.Active` from
  running and leaving the `ExecutionListener` unattached.
- `State.SetDefaults` and `State.Configure` in the NinjaScript stub now
  swallow failures so a broken bars subscription cannot block execution
  capture.
- Executions are now captured from every NT8 account, not just the first
  non-`Sim101` one. Previously, having a connected live account alongside
  the sim account caused sim fills to be silently dropped.
- `ExecutionListener` rolls `SessionState.CurrentDay` over at midnight so
  fills on NT8 sessions left running across days no longer attach to the
  previous day's `TradingDay` row.

### Added
- `FrontMonthResolver` (NTAddon) resolves the front-month ES contract at
  runtime, advancing 8 days before each quarterly expiry. The stub now
  rolls forward automatically each quarter.
- `AddonLog` writes the AddOn's lifecycle and errors to
  `Documents\NinjaTrader 8\ES.Trading\logs\addon.log`, so silent
  initialization failures leave a trail outside NT8's Output tab.
- `SessionState.IsHealthy` / `InitErrorMessage` and a red `UnhealthyBanner`
  on the NT8 panel surface initialization failures in the UI. A fallback
  `MessageBox` appears if the panel itself never opened.
- New `Trades.ContractSymbol` column captures the full NT8 instrument
  name (e.g. `"ES 06-26"`) per trade. The existing `Instrument` column
  keeps its normalized `"ES"` / `"MES"` value for grouping in analytics
  queries.
- `DatabaseContext.AddColumnIfMissing` provides an idempotent migration
  path for column additions; runs automatically from `EnsureCreated`.

### Changed
- `ExecutionListener` constructor takes `IEnumerable<Account>` instead of
  a single `Account`, and subscribes `ExecutionUpdate` on each.
- Trade Log grid in the Desktop App: the `Instrument` column is now
  `Contract` (width 60 → 90), bound to `ContractDisplay` which shows the
  full contract symbol and falls back to `Instrument` for pre-migration
  rows.
- CSV export adds a `ContractSymbol` column between `Instrument` and
  `Direction`.

## [0.1.0] — 2026-05-17

Initial check-in of the ES Trading toolkit.

### Added
- `ES.Trading.Core`: SQLite-backed repositories for trades, trading days,
  macro levels, discipline checks, and configuration; Market Overview
  service with pluggable data sources.
- `ES.Trading.NTAddon`: NT8 AddOn host with OR calculator, alert service,
  and execution listener. Plain class wrapped by a NinjaScript stub in
  `deploy/NinjaScriptStub/`.
- `ES.Trading.DesktopApp`: WPF dashboard with Trade Log, Macro Levels,
  Market Overview, and Settings views.

[Unreleased]: https://github.com/loumossa/ES.Trading/compare/0.1.0...HEAD
[0.1.0]: https://github.com/loumossa/ES.Trading/releases/tag/0.1.0
