using System;
using System.Collections.Generic;
using System.Globalization;
using Dapper;
using ES.Trading.Core.Models;

namespace ES.Trading.Core.Data
{
    public class ConfigurationRepository
    {
        private readonly DatabaseContext _db;

        public ConfigurationRepository(DatabaseContext db)
        {
            _db = db;
        }

        /// <summary>Returns all configuration entries.</summary>
        public IEnumerable<ConfigurationEntry> GetAll()
        {
            using var conn = _db.OpenConnection();
            return conn.Query<ConfigurationEntry>(
                "SELECT Key, Value, Description FROM Configuration ORDER BY Key;");
        }

        /// <summary>Returns the raw string value for a key, or null if not found.</summary>
        public string? GetRaw(string key)
        {
            using var conn = _db.OpenConnection();
            return conn.QuerySingleOrDefault<string>(
                "SELECT Value FROM Configuration WHERE Key = @Key;",
                new { Key = key });
        }

        /// <summary>
        /// Returns a typed value for a key. Throws if the key is missing or the value
        /// cannot be converted to T.
        /// Supports: string, int, double, decimal, bool.
        /// </summary>
        public T Get<T>(string key)
        {
            var raw = GetRaw(key)
                ?? throw new KeyNotFoundException($"Configuration key '{key}' not found.");

            return ConvertValue<T>(raw);
        }

        /// <summary>Returns a typed value or the provided default if the key is missing.</summary>
        public T GetOrDefault<T>(string key, T defaultValue)
        {
            var raw = GetRaw(key);
            if (raw == null) return defaultValue;

            try
            {
                return ConvertValue<T>(raw);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>Upserts a configuration value. Preserves existing description.</summary>
        public void Set(string key, string value)
        {
            using var conn = _db.OpenConnection();
            conn.Execute(
                @"INSERT INTO Configuration (Key, Value) VALUES (@Key, @Value)
                  ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;",
                new { Key = key, Value = value });
        }

        /// <summary>Upserts a typed value by converting it to its string representation.</summary>
        public void Set<T>(string key, T value)
        {
            var str = value switch
            {
                bool b   => b.ToString().ToLowerInvariant(),
                double d => d.ToString(CultureInfo.InvariantCulture),
                decimal m => m.ToString(CultureInfo.InvariantCulture),
                _        => value?.ToString() ?? string.Empty
            };

            Set(key, str);
        }

        private static T ConvertValue<T>(string raw)
        {
            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            object converted = targetType switch
            {
                _ when targetType == typeof(string)  => raw,
                _ when targetType == typeof(int)     => int.Parse(raw, CultureInfo.InvariantCulture),
                _ when targetType == typeof(double)  => double.Parse(raw, CultureInfo.InvariantCulture),
                _ when targetType == typeof(decimal) => decimal.Parse(raw, CultureInfo.InvariantCulture),
                _ when targetType == typeof(bool)    => raw.ToLowerInvariant() is "true" or "1" or "yes",
                _ => throw new InvalidCastException(
                    $"Cannot convert configuration value '{raw}' to type {typeof(T).Name}.")
            };

            return (T)converted;
        }
    }
}
