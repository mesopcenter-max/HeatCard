# Furnace Temperature Snapshot Implementation

## Overview

This implementation stores live Modbus data as **complete furnace snapshots**. Instead of storing one row per channel per timestamp, all channels for a controller are stored together in a single snapshot row. The global `TemperatureHistory.PollIntervalSeconds` setting controls the polling interval (currently 5 seconds).

## Problem Solved

**Before:** Each channel temperature was stored in a separate database row with the same timestamp:
```
Timestamp          | FurnaceNo | Channel  | Temperature
2026-10-28 10:25:11| F1        | Channel1 | 1200.5
2026-10-28 10:25:11| F1        | Channel2 | 1201.2
2026-10-28 10:25:11| F1        | Channel3 | 1199.8
```

When querying "find when temperature reached 1200.5", you got individual channel records without guaranteed consistency of what ALL channels were at that moment.

**After:** All channel temperatures for a controller are stored in a single snapshot row:
```
Timestamp          | FurnaceNo | ChannelDataJson
2026-10-28 10:25:11| F1        | {"Channel1": 1200.5, "Channel2": 1201.2, "Channel3": 1199.8}
```

This ensures **complete furnace state consistency** - when you query by date and temperature, you get the accurate snapshot of ALL channels at that exact moment.

## Database Schema Changes

### New Table: `FurnaceTemperatureSnapshots`

**SQLite:**
```sql
CREATE TABLE FurnaceTemperatureSnapshots (
	Id INTEGER PRIMARY KEY AUTOINCREMENT,
	Timestamp TEXT NOT NULL,
	FurnaceNo TEXT NOT NULL,
	ControllerId TEXT NOT NULL,
	ChannelDataJson TEXT NOT NULL,
	Source TEXT NOT NULL DEFAULT 'Modbus'
);
CREATE INDEX IX_FurnaceTemperatureSnapshots_Search ON FurnaceTemperatureSnapshots(FurnaceNo, Timestamp);
```

**SQL Server:**
```sql
CREATE TABLE dbo.FurnaceTemperatureSnapshots (
	Id BIGINT IDENTITY(1,1) PRIMARY KEY,
	Timestamp DATETIME2 NOT NULL,
	FurnaceNo NVARCHAR(200) NOT NULL,
	ControllerId NVARCHAR(200) NOT NULL,
	ChannelDataJson NVARCHAR(MAX) NOT NULL,
	Source NVARCHAR(100) NOT NULL DEFAULT 'Modbus'
);
CREATE INDEX IX_FurnaceTemperatureSnapshots_Search ON dbo.FurnaceTemperatureSnapshots(FurnaceNo, Timestamp);
```

**ChannelDataJson Format:**
```json
{
  "Channel1": 1200.5,
  "Channel2": 1201.2,
  "Channel3": 1199.8
}
```

## Code Changes

### 1. New Model: `FurnaceTemperatureSnapshot`

**File:** `Data/TemperatureReading.cs`

```csharp
public sealed class FurnaceTemperatureSnapshot
{
	public long Id { get; set; }
	public DateTime Timestamp { get; set; }
	public string FurnaceNo { get; set; } = "";
	public string ControllerId { get; set; } = "";
	public string ChannelDataJson { get; set; } = "{}";
	public string Source { get; set; } = "Modbus";

	// Deserialize channel data
	public Dictionary<string, double> GetChannelData()
	{
		try
		{
			return JsonSerializer.Deserialize<Dictionary<string, double>>(ChannelDataJson) 
				?? new Dictionary<string, double>();
		}
		catch
		{
			return new Dictionary<string, double>();
		}
	}

	// Serialize channel data
	public void SetChannelData(Dictionary<string, double> data)
	{
		ChannelDataJson = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
	}
}
```

### 2. Storage Methods in `TemperatureHistoryStore`

**File:** `Services/TemperatureHistoryStore.cs`

#### InsertSnapshotAsync
Inserts a complete furnace snapshot (all channels) at once:

```csharp
public async Task InsertSnapshotAsync(FurnaceTemperatureSnapshot snapshot, CancellationToken ct = default)
```

#### SearchSnapshotAsync
Queries snapshots with optional filtering by channel and temperature range:

```csharp
public async Task<List<FurnaceTemperatureSnapshot>> SearchSnapshotAsync(
	string furnace,
	DateTime from,
	DateTime to,
	string? channelFilter = null,
	double? temperatureMin = null,
	double? temperatureMax = null,
	CancellationToken ct = default)
```

**Usage Examples:**

```csharp
// Get all snapshots for Furnace "F1" on a specific date
var snapshots = await store.SearchSnapshotAsync("F1", startDate, endDate);

// Get snapshots where Channel1 is between 1200 and 1250
var filtered = await store.SearchSnapshotAsync(
	"F1", startDate, endDate,
	channelFilter: "Channel1",
	temperatureMin: 1200,
	temperatureMax: 1250);

// Get snapshots where ANY channel is >= 1200
var highTemp = await store.SearchSnapshotAsync(
	"F1", startDate, endDate,
	temperatureMin: 1200);
```

### 3. Updated Modbus Polling Service

**File:** `Services/ModbusPollingService.cs`

Changed to create a single snapshot with all channels instead of individual readings:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
	while (!stoppingToken.IsCancellationRequested)
	{
		foreach (var controller in _options.Controllers.Where(x => x.Enabled))
		{
			try
			{
				var regs = await ReadRegistersAsync(controller, stoppingToken);
				var now = DateTime.Now;

				// Create all channel data at once
				var channelData = new Dictionary<string, double>();

				for (var i = 0; i < controller.ChannelOffsets.Length; i++)
				{
					// ... temperature calculation ...
					var channelName = $"{controller.ChannelPrefix}{channelNumber}";
					channelData[channelName] = value;
				}

				if (channelData.Count > 0)
				{
					var snapshot = new FurnaceTemperatureSnapshot
					{
						Timestamp = now,
						FurnaceNo = controller.FurnaceNo,
						ControllerId = controller.Id,
						Source = "Modbus"
					};
					snapshot.SetChannelData(channelData);

					// Store all channels in a single database row
					await _store.InsertSnapshotAsync(snapshot, stoppingToken);
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Modbus poll failed for controller {ControllerId}", controller.Id);
			}
		}

		// The global configuration controls the polling interval.
		var interval = Math.Max(0.01, _options.PollIntervalSeconds);
		await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
	}
}
```

## Benefits

### 1. **Consistency**
   - All channel data for a furnace at a moment is stored and retrieved together
   - No risk of missing or mismatched channels

### 2. **Query Accuracy**
   - When you search by date and temperature, you get the exact furnace state at that moment
   - All channels are correlated to the same timestamp

### 3. **Performance**
   - Single write operation per poll interval (all channels) vs. multiple writes
   - Reduced I/O operations
   - Faster queries with indexed Timestamp and FurnaceNo

### 4. **Configurable Polling**
   - Previously storing individual channel rows created too many database writes
   - Now you can poll at 10ms intervals efficiently with one snapshot write per furnace per interval

### 5. **Flexible Queries**
   - Search by specific channel temperature
   - Search by any channel matching temperature range
   - Search by furnace and date range

## Migration (If Existing Data)

If you have existing data in the old `TemperatureReadings` table, you can migrate to snapshots:

```csharp
// Pseudo-code for migration:
var oldReadings = await store.SearchAsync(furnace, from, to);

foreach (var group in oldReadings.GroupBy(r => new { r.Timestamp, r.FurnaceNo }))
{
	var snapshot = new FurnaceTemperatureSnapshot
	{
		Timestamp = group.Key.Timestamp,
		FurnaceNo = group.Key.FurnaceNo,
		ControllerId = group.First().ControllerId,
		Source = "HistoricalImport"
	};

	var channelData = group.ToDictionary(r => r.Channel, r => r.Temperature);
	snapshot.SetChannelData(channelData);

	await store.InsertSnapshotAsync(snapshot);
}
```

## Backward Compatibility

- The old `TemperatureReadings` table remains unchanged
- The new `FurnaceTemperatureSnapshots` table works alongside it
- You can gradually migrate queries to use snapshots
- Both storage methods can coexist if needed

## Configuration

No configuration changes needed. The database schema is automatically created on first run via:
- `EnsureSqliteSchema()` - For SQLite databases
- `EnsureSqlServerSchema()` - For SQL Server databases

## Testing

To verify the implementation:

```csharp
// Create a snapshot
var snapshot = new FurnaceTemperatureSnapshot
{
	Timestamp = DateTime.Now,
	FurnaceNo = "F1",
	ControllerId = "CTRL1",
	Source = "Modbus"
};

var channelData = new Dictionary<string, double>
{
	{ "Channel1", 1200.5 },
	{ "Channel2", 1201.2 },
	{ "Channel3", 1199.8 }
};

snapshot.SetChannelData(channelData);

// Store it
await store.InsertSnapshotAsync(snapshot);

// Retrieve it
var results = await store.SearchSnapshotAsync("F1", DateTime.Now.AddHours(-1), DateTime.Now);

// Access channel data
foreach (var result in results)
{
	var channels = result.GetChannelData();
	foreach (var (channel, temp) in channels)
	{
		Console.WriteLine($"{channel}: {temp}°C");
	}
}
```

## Future Enhancements

1. **Analytics Dashboard** - Visualize complete furnace states over time
2. **Temperature Correlation** - Analyze relationships between channels
3. **Alert Triggers** - React when multiple channels deviate simultaneously
4. **Data Compression** - Archive old snapshots efficiently
5. **Real-time Streaming** - WebSocket updates showing live furnace states
