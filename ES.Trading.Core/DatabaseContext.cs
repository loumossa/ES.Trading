using System;
using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ES.Trading.Core.Data
{
    /// <summary>
    /// Manages the SQLite connection string and schema lifecycle.
    /// Both the NT8 Add-On and the Desktop App construct one instance of this,
    /// pointing at the same database file path.
    ///
    /// Usage:
    ///   var db = new DatabaseContext(@"C:\TradingData\es_trading.db");
    ///   db.EnsureCreated();   // call once at startup
    ///   using var conn = db.OpenConnection();
    /// </summary>
    public class DatabaseContext
    {
        private readonly string _connectionString;

        public DatabaseContext(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Database path must not be empty.", nameof(databasePath));

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                // WAL mode: allows the NT8 Add-On (writer) and Desktop App (reader)
                // to access the DB simultaneously without blocking each other.
                Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true
            }.ToString();
        }

        /// <summary>Opens and returns a new SQLite connection. Caller is responsible for disposal.</summary>
        public IDbConnection OpenConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();

            // WAL mode must be set per-connection on first use.
            conn.Execute("PRAGMA journal_mode=WAL;");
            conn.Execute("PRAGMA foreign_keys=ON;");

            return conn;
        }

        /// <summary>
        /// Creates all tables and seeds Configuration if they don't exist, and
        /// runs lightweight schema migrations for columns added after the first
        /// release. Safe to call on every startup.
        /// </summary>
        public void EnsureCreated()
        {
            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();

            try
            {
                conn.Execute(Sql.CreateTradingDays, transaction: tx);
                conn.Execute(Sql.CreateMacroLevels, transaction: tx);
                conn.Execute(Sql.CreateTrades, transaction: tx);
                conn.Execute(Sql.CreateTradeLegs, transaction: tx);
                conn.Execute(Sql.CreateDisciplineChecks, transaction: tx);
                conn.Execute(Sql.CreateConfiguration, transaction: tx);
                conn.Execute(Sql.SeedConfiguration, transaction: tx);

                // Migrations for columns added after the original schema.
                // SQLite's CREATE TABLE IF NOT EXISTS doesn't add new columns to
                // existing tables, so each addition needs an explicit check.
                AddColumnIfMissing(conn, tx, "Trades", "ContractSymbol", "TEXT");

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Idempotently adds a column to a table. SQLite has no "IF NOT EXISTS"
        /// syntax for columns, so we inspect <c>PRAGMA table_info</c> first.
        /// </summary>
        private static void AddColumnIfMissing(
            IDbConnection conn, IDbTransaction tx,
            string table, string column, string columnType)
        {
            var existing = conn.Query<string>(
                $"SELECT name FROM pragma_table_info('{table}');",
                transaction: tx);

            foreach (var name in existing)
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    return;

            conn.Execute(
                $"ALTER TABLE {table} ADD COLUMN {column} {columnType};",
                transaction: tx);
        }

        private static class Sql
        {
            public const string CreateTradingDays = @"
                CREATE TABLE IF NOT EXISTS TradingDays (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date                TEXT    NOT NULL UNIQUE,
                    ORHigh              REAL,
                    ORLow               REAL,
                    IsNoTradeDay        INTEGER NOT NULL DEFAULT 0,
                    MoodRating          INTEGER,
                    DailyPL             REAL    NOT NULL DEFAULT 0.0,
                    AttemptCount        INTEGER NOT NULL DEFAULT 0,
                    DailyLossLimitHit   INTEGER NOT NULL DEFAULT 0,
                    Notes               TEXT
                );";

            public const string CreateMacroLevels = @"
                CREATE TABLE IF NOT EXISTS MacroLevels (
                    Id      INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date    TEXT    NOT NULL,
                    Label   TEXT    NOT NULL,
                    Price   REAL    NOT NULL
                );";

            // Note: ContractSymbol is the NT8 full instrument name (e.g. "ES 06-26")
            // captured at the time of the fill, while Instrument stays the
            // normalized root ("ES" or "MES") for grouping in analytics queries.
            public const string CreateTrades = @"
                CREATE TABLE IF NOT EXISTS Trades (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    DayId               INTEGER NOT NULL REFERENCES TradingDays(Id),
                    EntryTime           TEXT    NOT NULL,
                    Instrument          TEXT    NOT NULL DEFAULT 'ES',
                    ContractSymbol      TEXT,
                    Direction           TEXT    NOT NULL,
                    EntryPrice          REAL    NOT NULL,
                    StopPrice           REAL,
                    AttemptNumber       INTEGER NOT NULL DEFAULT 1,
                    SetupType           TEXT,
                    IsMESFractionalExit INTEGER NOT NULL DEFAULT 0,
                    TotalPL             REAL,
                    Notes               TEXT,
                    ScreenshotPath      TEXT,
                    TagsJson            TEXT
                );";

            public const string CreateTradeLegs = @"
                CREATE TABLE IF NOT EXISTS TradeLegs (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    TradeId         INTEGER NOT NULL REFERENCES Trades(Id),
                    LegType         TEXT    NOT NULL,
                    ExecutionTime   TEXT    NOT NULL,
                    Price           REAL    NOT NULL,
                    Contracts       INTEGER NOT NULL,
                    PLRealized      REAL
                );";

            public const string CreateDisciplineChecks = @"
                CREATE TABLE IF NOT EXISTS DisciplineChecks (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    TradeId     INTEGER REFERENCES Trades(Id),
                    DayId       INTEGER NOT NULL REFERENCES TradingDays(Id),
                    RuleKey     TEXT    NOT NULL,
                    Passed      INTEGER NOT NULL,
                    Notes       TEXT
                );";

            public const string CreateConfiguration = @"
                CREATE TABLE IF NOT EXISTS Configuration (
                    Key         TEXT PRIMARY KEY,
                    Value       TEXT NOT NULL,
                    Description TEXT
                );";

            // INSERT OR IGNORE: only seeds rows that don't already exist,
            // so user-modified values survive app restarts.
            public const string SeedConfiguration = @"
                INSERT OR IGNORE INTO Configuration (Key, Value, Description) VALUES
                    ('DailyLossLimitDollars', '800',  'Dollar amount triggering daily shutdown alert'),
                    ('MaxAttempts',           '4',    'Max trade attempts before shutdown alert'),
                    ('FixedStopTicks',        '5',    'Default stop in ticks (informational reference)'),
                    ('RotationHandles',       '15',   'Handles per rotation level from OR'),
                    ('PartialProfitHandles',  '4',    'Handles at which to take the pay-for-the-trade exit'),
                    ('ORWindowSeconds',       '30',   'Duration of OR capture window in seconds'),
                    ('AlertSoundEnabled',     'true',  'Audio alerts on NT8 panel'),
                    ('DisciplineRollingDays', '20',    'Rolling window for discipline score average'),
                    ('PanelAlwaysOnTop',      'false', 'NT8 panel stays above other windows');";
        }
    }
}
