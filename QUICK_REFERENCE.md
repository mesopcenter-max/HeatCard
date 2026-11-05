# Quick Reference Card

## Database Configuration
- **Provider:** SQL Server (LocalDB)
- **Server:** (localdb)\MSSQLLocalDB
- **Database:** HALHeatTreatmentCard
- **New Table:** FurnaceTemperatureSnapshots

## Table Schema

```
FurnaceTemperatureSnapshots
├── Id (BIGINT) - Auto-increment primary key
├── Timestamp (DATETIME2) - When snapshot was taken
├── FurnaceNo (NVARCHAR) - Furnace identifier (indexed)
├── ControllerId (NVARCHAR) - Controller ID
├── ChannelDataJson (NVARCHAR(MAX)) - {"Channel1": 1200.5, ...}
└── Source (NVARCHAR) - Default: "Modbus"

Indexes: (FurnaceNo, Timestamp)
```

## Data Format Example

```json
{
  "Timestamp": "2026-10-28 10:30:43",
  "FurnaceNo": "F1",
  "ControllerId": "CTRL1",
  "ChannelDataJson": {
	"Channel1": 1200.5,
	"Channel2": 1201.2,
	"Channel3": 1199.8
  },
  "Source": "Modbus"
}
```

## Core API Methods

### Insert Snapshot
```csharp
await store.InsertSnapshotAsync(snapshot, cancellationToken);
```

### Search Snapshots
```csharp
var snapshots = await store.SearchSnapshotAsync(
	furnace: "F1",
	from: DateTime.Now.AddHours(-1),
	to: DateTime.Now,
	channelFilter: "Channel1",        // Optional
	temperatureMin: 1200,              // Optional
	temperatureMax: 1250,              // Optional
	cancellationToken
);
```

## Working with Channel Data

### Get Channel Dictionary
```csharp
var snapshot = snapshots.First();
var channels = snapshot.GetChannelData();
// Returns: Dictionary<string, double>
// Example: {"Channel1": 1200.5, "Channel2": 1201.2}
```

### Set Channel Dictionary
```csharp
var snapshot = new FurnaceTemperatureSnapshot();
var channelData = new Dictionary<string, double>
{
	{ "Channel1", 1200.5 },
	{ "Channel2", 1201.2 }
};
snapshot.SetChannelData(channelData);
```

## Common Queries

### Get Latest Snapshot
```csharp
var snapshots = await store.SearchSnapshotAsync(
	"F1", 
	DateTime.Now.AddHours(-1), 
	DateTime.Now
);
var latest = snapshots.FirstOrDefault();
```

### Find When Temperature Reached
```csharp
var snapshots = await store.SearchSnapshotAsync(
	"F1",
	DateTime.Now.Date,
	DateTime.Now.Date.AddDays(1),
	channelFilter: "Channel1",
	temperatureMin: 1200
);
var firstTime = snapshots.LastOrDefault()?.Timestamp; // Last in DESC order
```

### Get All Channels at Specific Time
```csharp
var snapshots = await store.SearchSnapshotAsync(
	"F1",
	targetTime - TimeSpan.FromSeconds(5),
	targetTime + TimeSpan.FromSeconds(5)
);
var snapshot = snapshots.FirstOrDefault();
var allChannels = snapshot?.GetChannelData();
```

### Find Temperature Deviation
```csharp
var snapshots = await store.SearchSnapshotAsync("F1", from, to);
foreach (var snapshot in snapshots)
{
	var channels = snapshot.GetChannelData();
	var avgTemp = channels.Values.Average();
	var maxDeviation = channels.Values.Max(t => Math.Abs(t - avgTemp));

	if (maxDeviation > 10)
	{
		Console.WriteLine($"High deviation at {snapshot.Timestamp}: {maxDeviation}°C");
	}
}
```

## SQL Queries

### Count Snapshots
```sql
SELECT COUNT(*) FROM dbo.FurnaceTemperatureSnapshots
WHERE FurnaceNo = 'F1'
```

### Get Today's Data
```sql
SELECT * FROM dbo.FurnaceTemperatureSnapshots
WHERE FurnaceNo = 'F1'
  AND CAST(Timestamp AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY Timestamp DESC
```

### Get Recent Activity (Last Hour)
```sql
SELECT Timestamp, FurnaceNo, ChannelDataJson
FROM dbo.FurnaceTemperatureSnapshots
WHERE Timestamp >= DATEADD(HOUR, -1, GETDATE())
ORDER BY Timestamp DESC
```

### Get Snapshots Between Times
```sql
SELECT * FROM dbo.FurnaceTemperatureSnapshots
WHERE FurnaceNo = 'F1'
  AND Timestamp >= '2026-10-28 09:00:00'
  AND Timestamp <= '2026-10-28 11:00:00'
ORDER BY Timestamp DESC
```

## Polling Configuration

**File:** `appsettings.json`

```json
{
  "TemperatureHistory": {
	"DatabaseProvider": "SqlServer",
	"PollIntervalSeconds": 5,
	"ConnectTimeoutSeconds": 3,
	"Controllers": [
	  {
		"Id": "GH-3",
		"FurnaceNo": "GH-3",
		"Host": "172.133.230.7",
		"Port": 502,
		"Enabled": true,
		"ChannelOffsets": [0, 1],
		"ChannelPrefix": "CH"
	  }
	]
  }
}
```

## Troubleshooting

### No Data Appearing
1. Check Modbus device connectivity: `Test-NetConnection -ComputerName 172.133.230.7 -Port 502`
2. Verify appsettings.json has correct IP addresses
3. Confirm table was created: `SELECT * FROM dbo.FurnaceTemperatureSnapshots`

### JSON Parsing Error
```csharp
try
{
	var channels = snapshot.GetChannelData();
}
catch
{
	// Invalid JSON - log and skip
}
```

### No Results from Search
- Verify furnace name matches exactly (case-sensitive)
- Check date range includes actual data
- Ensure Modbus devices are online

## Performance Tips

1. **Always use date filters** - Don't query entire history
2. **Limit results** - Use channelFilter when possible
3. **Batch operations** - Process in daily chunks
4. **Index awareness** - Queries on (FurnaceNo, Timestamp) are fast
5. **Archive old data** - Consider deleting data older than 30-90 days

## Files to Reference

- `Data/TemperatureReading.cs` - Class definition
- `Services/TemperatureHistoryStore.cs` - Storage implementation
- `FURNACE_SNAPSHOT_IMPLEMENTATION.md` - Technical details
- `QUERY_SNAPSHOTS_GUIDE.md` - Usage examples
- `DATABASE_STORAGE_STATUS.md` - Verification results
