using HeatCardGenerator.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace HeatCardGenerator.Services;

public sealed class TemperatureHistoryStore
{
    private readonly bool _useSqlServer;
    private readonly string _connectionString;

    public TemperatureHistoryStore(IOptions<TemperatureHistoryOptions> options, IWebHostEnvironment env)
    {
        var config = options.Value;
        _useSqlServer = string.Equals(config.DatabaseProvider, "SqlServer", StringComparison.OrdinalIgnoreCase);

        if (_useSqlServer)
        {
            _connectionString = config.DefaultConnection;
            EnsureSqlServerSchema();
            return;
        }

        var path = config.DatabasePath;
        if (!Path.IsPathRooted(path)) path = Path.Combine(env.ContentRootPath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
        EnsureSqliteSchema();
    }

    private void EnsureSqliteSchema()
    {
        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS TemperatureReadings (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 Timestamp TEXT NOT NULL,
 FurnaceNo TEXT NOT NULL,
 ControllerId TEXT NOT NULL,
 Channel TEXT NOT NULL,
 Temperature REAL NOT NULL,
 ActualTemp REAL NOT NULL DEFAULT 0,
 Source TEXT NOT NULL DEFAULT 'Modbus');
CREATE INDEX IF NOT EXISTS IX_TemperatureReadings_Search ON TemperatureReadings(FurnaceNo, Timestamp, Channel);

CREATE TABLE IF NOT EXISTS FurnaceTemperatureSnapshots (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 Timestamp TEXT NOT NULL,
 FurnaceNo TEXT NOT NULL,
 ControllerId TEXT NOT NULL,
 ChannelDataJson TEXT NOT NULL,
 Source TEXT NOT NULL DEFAULT 'Modbus');
CREATE INDEX IF NOT EXISTS IX_FurnaceTemperatureSnapshots_Search ON FurnaceTemperatureSnapshots(FurnaceNo, Timestamp);";
        cmd.ExecuteNonQuery();
        using var migrate = c.CreateCommand();
        migrate.CommandText = @"PRAGMA table_info(TemperatureReadings);";
        using var reader = migrate.ExecuteReader();
        var hasActualTemp = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "ActualTemp", StringComparison.OrdinalIgnoreCase))
            {
                hasActualTemp = true;
                break;
            }
        }
        reader.Close();
        if (!hasActualTemp)
        {
            using var alter = c.CreateCommand();
            alter.CommandText = "ALTER TABLE TemperatureReadings ADD COLUMN ActualTemp REAL NOT NULL DEFAULT 0; UPDATE TemperatureReadings SET ActualTemp = Temperature;";
            alter.ExecuteNonQuery();
        }
    }

    private void EnsureSqlServerSchema()
    {
        var builder = new SqlConnectionStringBuilder(_connectionString);
        var databaseName = builder.InitialCatalog ?? "HALHeatTreatmentCard";
        builder.InitialCatalog = "master";

        using (var c = new SqlConnection(builder.ConnectionString))
        {
            c.Open();
            using var ensureDb = c.CreateCommand();
            ensureDb.CommandText = $@"IF DB_ID('{databaseName.Replace("'", "''")}') IS NULL
BEGIN
    EXEC('CREATE DATABASE [{databaseName.Replace("'", "''")}];');
END;";
            ensureDb.ExecuteNonQuery();
        }

        using var db = new SqlConnection(_connectionString);
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
IF OBJECT_ID(N'dbo.TemperatureReadings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TemperatureReadings (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        Timestamp DATETIME2 NOT NULL,
        FurnaceNo NVARCHAR(200) NOT NULL,
        ControllerId NVARCHAR(200) NOT NULL,
        Channel NVARCHAR(100) NOT NULL,
        Temperature DECIMAL(9,3) NOT NULL,
        ActualTemp DECIMAL(9,3) NOT NULL DEFAULT 0,
        Source NVARCHAR(100) NOT NULL DEFAULT 'Modbus'
    );
END;
IF COL_LENGTH(N'dbo.TemperatureReadings', N'ActualTemp') IS NULL
BEGIN
    ALTER TABLE dbo.TemperatureReadings ADD ActualTemp DECIMAL(9,3) NOT NULL DEFAULT 0;
    EXEC(N'UPDATE dbo.TemperatureReadings SET ActualTemp = Temperature;');
END;
UPDATE dbo.TemperatureReadings
SET ActualTemp = Temperature / 10.0
WHERE Source = N'Modbus' AND ActualTemp = Temperature;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TemperatureReadings_Search' AND object_id = OBJECT_ID('dbo.TemperatureReadings'))
BEGIN
    CREATE INDEX IX_TemperatureReadings_Search ON dbo.TemperatureReadings(FurnaceNo, Timestamp, Channel);
END;

IF OBJECT_ID(N'dbo.FurnaceTemperatureSnapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FurnaceTemperatureSnapshots (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        Timestamp DATETIME2 NOT NULL,
        FurnaceNo NVARCHAR(200) NOT NULL,
        ControllerId NVARCHAR(200) NOT NULL,
        ChannelDataJson NVARCHAR(MAX) NOT NULL,
        Source NVARCHAR(100) NOT NULL DEFAULT 'Modbus'
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FurnaceTemperatureSnapshots_Search' AND object_id = OBJECT_ID('dbo.FurnaceTemperatureSnapshots'))
BEGIN
    CREATE INDEX IX_FurnaceTemperatureSnapshots_Search ON dbo.FurnaceTemperatureSnapshots(FurnaceNo, Timestamp);
END;";
        cmd.ExecuteNonQuery();
    }

    public static string GetSqlServerMasterConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        builder.InitialCatalog = "master";
        return builder.ConnectionString;
    }

    /// <summary>
    /// Insert a furnace temperature snapshot with all channel data at once.
    /// </summary>
    public async Task InsertSnapshotAsync(FurnaceTemperatureSnapshot snapshot, CancellationToken ct = default)
    {
        if (_useSqlServer)
        {
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO dbo.FurnaceTemperatureSnapshots(Timestamp,FurnaceNo,ControllerId,ChannelDataJson,Source)
VALUES(@timestamp,@furnaceNo,@controllerId,@channelDataJson,@source);";
            cmd.Parameters.AddWithValue("@timestamp", snapshot.Timestamp);
            cmd.Parameters.AddWithValue("@furnaceNo", snapshot.FurnaceNo ?? string.Empty);
            cmd.Parameters.AddWithValue("@controllerId", snapshot.ControllerId ?? string.Empty);
            cmd.Parameters.AddWithValue("@channelDataJson", snapshot.ChannelDataJson);
            cmd.Parameters.AddWithValue("@source", snapshot.Source ?? "Modbus");
            await cmd.ExecuteNonQueryAsync(ct);
            return;
        }

        await using var sqlite = new SqliteConnection(_connectionString);
        await sqlite.OpenAsync(ct);
        await using var cmdSqlite = sqlite.CreateCommand();
        cmdSqlite.CommandText = "INSERT INTO FurnaceTemperatureSnapshots(Timestamp,FurnaceNo,ControllerId,ChannelDataJson,Source) VALUES($t,$f,$c,$ch,$s)";
        cmdSqlite.Parameters.AddWithValue("$t", snapshot.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        cmdSqlite.Parameters.AddWithValue("$f", snapshot.FurnaceNo);
        cmdSqlite.Parameters.AddWithValue("$c", snapshot.ControllerId);
        cmdSqlite.Parameters.AddWithValue("$ch", snapshot.ChannelDataJson);
        cmdSqlite.Parameters.AddWithValue("$s", snapshot.Source);
        await cmdSqlite.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Search for furnace temperature snapshots by furnace, date range, and optionally by a specific channel temperature.
    /// Returns snapshots that match the temperature range for the specified channel.
    /// </summary>
    public async Task<List<FurnaceTemperatureSnapshot>> SearchSnapshotAsync(string furnace, DateTime from, DateTime to, 
        string? channelFilter = null, double? temperatureMin = null, double? temperatureMax = null, CancellationToken ct = default)
    {
        var result = new List<FurnaceTemperatureSnapshot>();

        if (_useSqlServer)
        {
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT Id,Timestamp,FurnaceNo,ControllerId,ChannelDataJson,Source
FROM dbo.FurnaceTemperatureSnapshots
WHERE (@furnace = '' OR FurnaceNo = @furnace)
  AND Timestamp >= @from
  AND Timestamp < @to
ORDER BY Timestamp DESC;";
            cmd.Parameters.AddWithValue("@furnace", string.IsNullOrWhiteSpace(furnace) ? string.Empty : furnace.Trim());
            cmd.Parameters.AddWithValue("@from", from);
            cmd.Parameters.AddWithValue("@to", to);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var snapshot = new FurnaceTemperatureSnapshot
                {
                    Id = rd.GetInt64(0),
                    Timestamp = rd.GetDateTime(1),
                    FurnaceNo = rd.GetString(2),
                    ControllerId = rd.GetString(3),
                    ChannelDataJson = rd.GetString(4),
                    Source = rd.IsDBNull(5) ? "Modbus" : rd.GetString(5)
                };

                if (ShouldIncludeSnapshot(snapshot, channelFilter, temperatureMin, temperatureMax))
                {
                    result.Add(snapshot);
                }
            }
            return result;
        }

        await using var sqlite = new SqliteConnection(_connectionString);
        await sqlite.OpenAsync(ct);
        await using var command = sqlite.CreateCommand();
        command.CommandText = @"SELECT Id,Timestamp,FurnaceNo,ControllerId,ChannelDataJson,Source 
FROM FurnaceTemperatureSnapshots
WHERE ($f='' OR FurnaceNo=$f) AND Timestamp >= $from AND Timestamp < $to 
ORDER BY Timestamp DESC;";
        command.Parameters.AddWithValue("$f", furnace.Trim());
        command.Parameters.AddWithValue("$from", from.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", to.ToString("O", CultureInfo.InvariantCulture));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var snapshot = new FurnaceTemperatureSnapshot
            {
                Id = reader.GetInt64(0),
                Timestamp = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                FurnaceNo = reader.GetString(2),
                ControllerId = reader.GetString(3),
                ChannelDataJson = reader.GetString(4),
                Source = reader.GetString(5)
            };

            if (ShouldIncludeSnapshot(snapshot, channelFilter, temperatureMin, temperatureMax))
            {
                result.Add(snapshot);
            }
        }
        return result;
    }

    private static bool ShouldIncludeSnapshot(FurnaceTemperatureSnapshot snapshot, 
        string? channelFilter, double? temperatureMin, double? temperatureMax)
    {
        if (string.IsNullOrWhiteSpace(channelFilter) && !temperatureMin.HasValue && !temperatureMax.HasValue)
            return true;

        var channelData = snapshot.GetChannelData();

        if (!string.IsNullOrWhiteSpace(channelFilter))
        {
            if (!channelData.TryGetValue(channelFilter, out var temp))
                return false;

            if (temperatureMin.HasValue && temp < temperatureMin.Value)
                return false;

            if (temperatureMax.HasValue && temp > temperatureMax.Value)
                return false;

            return true;
        }

        // If no specific channel filter, check if ANY channel matches the temperature range
        if (temperatureMin.HasValue || temperatureMax.HasValue)
        {
            return channelData.Values.Any(temp =>
                (!temperatureMin.HasValue || temp >= temperatureMin.Value) &&
                (!temperatureMax.HasValue || temp <= temperatureMax.Value));
        }

        return true;
    }

    public async Task InsertManyAsync(IEnumerable<TemperatureReading> readings, CancellationToken ct = default)
    {
        var batch = readings.ToList();
        if (batch.Count == 0) return;

        if (_useSqlServer)
        {
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync(ct);
            foreach (var r in batch)
            {
                await using var cmd = c.CreateCommand();
                cmd.CommandText = @"INSERT INTO dbo.TemperatureReadings(Timestamp,FurnaceNo,ControllerId,Channel,Temperature,ActualTemp,Source)
VALUES(@timestamp,@furnaceNo,@controllerId,@channel,@temperature,@actualTemp,@source);";
                cmd.Parameters.AddWithValue("@timestamp", r.Timestamp);
                cmd.Parameters.AddWithValue("@furnaceNo", r.FurnaceNo ?? string.Empty);
                cmd.Parameters.AddWithValue("@controllerId", r.ControllerId ?? string.Empty);
                cmd.Parameters.AddWithValue("@channel", r.Channel ?? string.Empty);
                cmd.Parameters.AddWithValue("@temperature", r.Temperature);
                cmd.Parameters.AddWithValue("@actualTemp", r.ActualTemp);
                cmd.Parameters.AddWithValue("@source", r.Source ?? "Modbus");
                await cmd.ExecuteNonQueryAsync(ct);
            }
            return;
        }

        await using var sql = new SqliteConnection(_connectionString);
        await sql.OpenAsync(ct);
        await using var tx = await sql.BeginTransactionAsync(ct);
        foreach (var r in batch)
        {
            await using var cmd = sql.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "INSERT INTO TemperatureReadings(Timestamp,FurnaceNo,ControllerId,Channel,Temperature,ActualTemp,Source) VALUES($t,$f,$c,$ch,$v,$a,$s)";
            cmd.Parameters.AddWithValue("$t", r.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$f", r.FurnaceNo);
            cmd.Parameters.AddWithValue("$c", r.ControllerId);
            cmd.Parameters.AddWithValue("$ch", r.Channel);
            cmd.Parameters.AddWithValue("$v", r.Temperature);
            cmd.Parameters.AddWithValue("$a", r.ActualTemp);
            cmd.Parameters.AddWithValue("$s", r.Source);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<List<TemperatureReading>> SearchAsync(string furnace, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var result = new List<TemperatureReading>();
        if (_useSqlServer)
        {
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT Id,Timestamp,FurnaceNo,ControllerId,Channel,Temperature,ActualTemp,Source
FROM dbo.TemperatureReadings
WHERE (@furnace = '' OR FurnaceNo = @furnace)
  AND Timestamp >= @from
  AND Timestamp < @to
ORDER BY Timestamp, Channel;";
            cmd.Parameters.AddWithValue("@furnace", string.IsNullOrWhiteSpace(furnace) ? string.Empty : furnace.Trim());
            cmd.Parameters.AddWithValue("@from", from);
            cmd.Parameters.AddWithValue("@to", to);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                result.Add(new TemperatureReading
                {
                    Id = rd.GetInt64(0),
                    Timestamp = rd.GetDateTime(1),
                    FurnaceNo = rd.GetString(2),
                    ControllerId = rd.GetString(3),
                    Channel = rd.GetString(4),
                    Temperature = Convert.ToDouble(rd.GetDecimal(5)),
                    ActualTemp = Convert.ToDouble(rd.GetDecimal(6)),
                    Source = rd.IsDBNull(7) ? "HistoricalImport" : rd.GetString(7)
                });
            }
        }
        else
        {
            await using var sqlite = new SqliteConnection(_connectionString);
            await sqlite.OpenAsync(ct);
            await using var command = sqlite.CreateCommand();
            command.CommandText = @"SELECT Id,Timestamp,FurnaceNo,ControllerId,Channel,Temperature,ActualTemp,Source FROM TemperatureReadings
WHERE ($f='' OR FurnaceNo=$f) AND Timestamp >= $from AND Timestamp < $to ORDER BY Timestamp,Channel";
            command.Parameters.AddWithValue("$f", furnace.Trim());
            command.Parameters.AddWithValue("$from", from.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$to", to.ToString("O", CultureInfo.InvariantCulture));
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new TemperatureReading
                {
                    Id = reader.GetInt64(0),
                    Timestamp = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    FurnaceNo = reader.GetString(2),
                    ControllerId = reader.GetString(3),
                    Channel = reader.GetString(4),
                    Temperature = reader.GetDouble(5),
                    ActualTemp = reader.GetDouble(6),
                    Source = reader.GetString(7)
                });
            }
        }

        var snapshots = await SearchSnapshotAsync(furnace, from, to, ct: ct);
        var existingReadings = result
            .Select(reading => (reading.Timestamp, reading.FurnaceNo, reading.ControllerId, reading.Channel))
            .ToHashSet();

        foreach (var snapshot in snapshots)
        {
            foreach (var channel in snapshot.GetChannelData())
            {
                var key = (snapshot.Timestamp, snapshot.FurnaceNo, snapshot.ControllerId, channel.Key);
                if (!existingReadings.Add(key)) continue;

                var actualTemp = string.Equals(snapshot.Source, "Modbus", StringComparison.OrdinalIgnoreCase)
                    ? channel.Value / 10.0
                    : channel.Value;

                result.Add(new TemperatureReading
                {
                    Id = snapshot.Id,
                    Timestamp = snapshot.Timestamp,
                    FurnaceNo = snapshot.FurnaceNo,
                    ControllerId = snapshot.ControllerId,
                    Channel = channel.Key,
                    Temperature = actualTemp,
                    ActualTemp = actualTemp,
                    Source = snapshot.Source
                });
            }
        }

        return result.OrderBy(reading => reading.Timestamp).ThenBy(reading => reading.Channel).ToList();
    }

    public async Task<int> ImportCsvAsync(Stream stream, CancellationToken ct = default)
    {
        var readings = await ParseCsvAsync(stream, ct);
        await InsertManyAsync(readings, ct);
        return readings.Count;
    }

    public static async Task<List<TemperatureReading>> ParseCsvAsync(Stream stream, CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream);
        var first = true;
        var batch = new List<TemperatureReading>();

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (first)
            {
                first = false;
                if (line.Contains("timestamp", StringComparison.OrdinalIgnoreCase)) continue;
            }

            var parts = line.Split(',');
            if (parts.Length < 4) continue;
            if (!DateTime.TryParse(parts[0].Trim().Trim('"'), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var ts)) continue;
            var furnace = parts[1].Trim().Trim('"');
            var channel = parts[2].Trim().Trim('"');
            if (!double.TryParse(parts[3].Trim().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out var temp)) continue;
            var controller = parts.Length > 4 ? parts[4].Trim().Trim('"') : "HistoricalImport";
            batch.Add(new TemperatureReading { Timestamp = ts, FurnaceNo = furnace, Channel = channel, ControllerId = controller, Temperature = temp, ActualTemp = temp, Source = "HistoricalImport" });
        }

        return batch;
    }

    public static List<HeatTimeResult> Calculate(IEnumerable<TemperatureReading> source, double target)
    {
        var output = new List<HeatTimeResult>();

        foreach (var group in source.GroupBy(x => new { x.FurnaceNo, x.ControllerId, x.Channel }))
        {
            var rows = group.OrderBy(x => x.Timestamp).ToList();
            if (rows.Count == 0) continue;

            var exactEntry = rows.FirstOrDefault(x => x.ActualTemp >= target);
            var nearest = rows.OrderBy(x => Math.Abs(x.ActualTemp - target)).FirstOrDefault();

            DateTime? inTime = null;
            DateTime? outTime = null;
            string status;

            if (exactEntry != null)
            {
                inTime = exactEntry.Timestamp;
                outTime = rows.Where(x => x.Timestamp > exactEntry.Timestamp)
                    .OrderBy(x => x.Timestamp)
                    .FirstOrDefault(x => x.ActualTemp < target)?.Timestamp;
                status = outTime.HasValue ? "Exact match" : "Reached; no out time in selected period";
            }
            else if (nearest != null)
            {
                inTime = nearest.Timestamp;
                outTime = rows.Where(x => x.Timestamp > nearest.Timestamp)
                    .OrderBy(x => x.Timestamp)
                    .FirstOrDefault(x => x.ActualTemp <= target || Math.Abs(x.ActualTemp - target) < Math.Abs(nearest.ActualTemp - target))?.Timestamp;
                status = outTime.HasValue ? "Nearest match" : "Nearest reading; no fall below target in selected period";
            }
            else
            {
                status = "Not reached";
            }

            output.Add(new HeatTimeResult
            {
                FurnaceNo = group.Key.FurnaceNo,
                ControllerId = group.Key.ControllerId,
                Channel = group.Key.Channel,
                TargetTemperature = target,
                InTime = inTime,
                OutTime = outTime,
                Status = status
            });
        }

        return output.OrderBy(x => x.FurnaceNo).ThenBy(x => x.ControllerId).ThenBy(x => x.Channel).ToList();
    }
}

