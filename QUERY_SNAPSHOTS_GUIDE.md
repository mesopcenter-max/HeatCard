# How to Query FurnaceTemperatureSnapshots Data

This guide shows how to use the new snapshot storage system to query furnace temperature data by date and temperature.

## Basic Usage

### 1. Get All Snapshots for a Furnace

```csharp
using HeatCardGenerator.Services;
using HeatCardGenerator.Data;

// Inject TemperatureHistoryStore into your controller/service
public class YourService
{
	private readonly TemperatureHistoryStore _store;

	public async Task GetFurnaceData()
	{
		// Get all snapshots for furnace "F1" today
		var startOfDay = DateTime.Now.Date;
		var endOfDay = startOfDay.AddDays(1);

		var snapshots = await _store.SearchSnapshotAsync(
			furnace: "F1",
			from: startOfDay,
			to: endOfDay
		);

		foreach (var snapshot in snapshots)
		{
			var channels = snapshot.GetChannelData();
			Console.WriteLine($"[{snapshot.Timestamp}] Furnace: {snapshot.FurnaceNo}");
			foreach (var (channel, temperature) in channels)
			{
				Console.WriteLine($"  {channel}: {temperature}°C");
			}
		}
	}
}
```

### 2. Search by Specific Channel Temperature

```csharp
// Find all snapshots where Channel1 is between 1200 and 1250
var snapshots = await _store.SearchSnapshotAsync(
	furnace: "F1",
	from: DateTime.Now.AddHours(-1),
	to: DateTime.Now,
	channelFilter: "Channel1",
	temperatureMin: 1200,
	temperatureMax: 1250
);

foreach (var snapshot in snapshots)
{
	var channels = snapshot.GetChannelData();
	var channel1Temp = channels["Channel1"];
	Console.WriteLine($"[{snapshot.Timestamp}] Channel1: {channel1Temp}°C (Furnace: {snapshot.FurnaceNo})");
}
```

### 3. Find When Temperature Reached Target

```csharp
public async Task<DateTime?> FindTargetTemperatureTime(string furnaceNo, double targetTemp, string channel)
{
	var today = DateTime.Now.Date;
	var tomorrow = today.AddDays(1);

	var snapshots = await _store.SearchSnapshotAsync(
		furnace: furnaceNo,
		from: today,
		to: tomorrow,
		channelFilter: channel,
		temperatureMin: targetTemp
	);

	if (snapshots.Count > 0)
	{
		var firstMatch = snapshots.Last(); // Earliest time (ordered DESC, so last is earliest)
		return firstMatch.Timestamp;
	}

	return null;
}

// Usage
var timeReached = await FindTargetTemperatureTime("F1", 1200, "Channel1");
if (timeReached.HasValue)
{
	Console.WriteLine($"Temperature reached 1200°C at {timeReached}");
}
```

### 4. Get Latest Snapshot

```csharp
public async Task<FurnaceTemperatureSnapshot?> GetLatestSnapshot(string furnaceNo)
{
	var lastHour = DateTime.Now.AddHours(-1);
	var now = DateTime.Now;

	var snapshots = await _store.SearchSnapshotAsync(
		furnace: furnaceNo,
		from: lastHour,
		to: now
	);

	return snapshots.FirstOrDefault();
}

// Usage
var latest = await GetLatestSnapshot("F1");
if (latest != null)
{
	var channels = latest.GetChannelData();
	Console.WriteLine($"Latest temperature data for F1:");
	foreach (var (ch, temp) in channels)
	{
		Console.WriteLine($"  {ch}: {temp}°C");
	}
}
```

## Advanced Scenarios

### 5. Compare All Channels at a Specific Time

```csharp
public async Task AnalyzeFurnaceState(string furnaceNo, DateTime targetTime)
{
	var timeWindow = TimeSpan.FromSeconds(5); // 5-second window
	var snapshots = await _store.SearchSnapshotAsync(
		furnace: furnaceNo,
		from: targetTime - timeWindow,
		to: targetTime + timeWindow
	);

	if (snapshots.Any())
	{
		var snapshot = snapshots.First(); // Closest to target time
		var channels = snapshot.GetChannelData();

		Console.WriteLine($"Furnace State at {snapshot.Timestamp}:");

		// Find min/max temperatures
		var minTemp = channels.Values.Min();
		var maxTemp = channels.Values.Max();
		var avgTemp = channels.Values.Average();

		Console.WriteLine($"  Min: {minTemp}°C, Max: {maxTemp}°C, Avg: {avgTemp:F1}°C");
		Console.WriteLine($"  Channel Details:");

		foreach (var (ch, temp) in channels.OrderBy(x => x.Key))
		{
			var deviation = temp - avgTemp;
			var deviationStr = deviation >= 0 ? $"+{deviation:F1}" : $"{deviation:F1}";
			Console.WriteLine($"    {ch}: {temp}°C ({deviationStr})");
		}
	}
}
```

### 6. Find Temperature Anomalies

```csharp
public async Task DetectAnomalies(string furnaceNo, double maxDeviation)
{
	var today = DateTime.Now.Date;
	var tomorrow = today.AddDays(1);

	var snapshots = await _store.SearchSnapshotAsync(
		furnace: furnaceNo,
		from: today,
		to: tomorrow
	);

	foreach (var snapshot in snapshots)
	{
		var channels = snapshot.GetChannelData();
		var avgTemp = channels.Values.Average();

		foreach (var (channel, temp) in channels)
		{
			var deviation = Math.Abs(temp - avgTemp);
			if (deviation > maxDeviation)
			{
				Console.WriteLine($"⚠️  ANOMALY at {snapshot.Timestamp}");
				Console.WriteLine($"   {channel}: {temp}°C (deviation: {deviation:F1}°C from avg {avgTemp:F1}°C)");
			}
		}
	}
}

// Usage: Find channels deviating more than 10°C from average
await DetectAnomalies("F1", maxDeviation: 10);
```

### 7. Export Data to CSV

```csharp
using System.Text;

public async Task ExportSnapshotsToCSV(string furnaceNo, DateTime from, DateTime to, string filePath)
{
	var snapshots = await _store.SearchSnapshotAsync(furnaceNo, from, to);

	var sb = new StringBuilder();

	if (snapshots.Any())
	{
		var firstSnapshot = snapshots.Last(); // Get one snapshot to determine channels
		var channels = firstSnapshot.GetChannelData().Keys.ToList();

		// Write header
		sb.AppendLine($"Timestamp,FurnaceNo,ControllerId,{string.Join(",", channels)}");

		// Write data
		foreach (var snapshot in snapshots.OrderBy(s => s.Timestamp))
		{
			var channelData = snapshot.GetChannelData();
			var values = channels.Select(ch => channelData.ContainsKey(ch) ? channelData[ch].ToString("F2") : "");
			sb.AppendLine($"{snapshot.Timestamp},{snapshot.FurnaceNo},{snapshot.ControllerId},{string.Join(",", values)}");
		}
	}

	await File.WriteAllTextAsync(filePath, sb.ToString());
	Console.WriteLine($"Data exported to {filePath}");
}

// Usage
await ExportSnapshotsToCSV("F1", DateTime.Now.AddDays(-1), DateTime.Now, "furnace_data.csv");
```

## Working with Raw SQL

If you need to query directly with SQL:

### Query 1: All snapshots for a furnace today
```sql
SELECT Id, Timestamp, FurnaceNo, ControllerId, ChannelDataJson
FROM dbo.FurnaceTemperatureSnapshots
WHERE FurnaceNo = 'F1'
  AND CAST(Timestamp AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY Timestamp DESC
```

### Query 2: Find when specific channel reached temperature
```sql
DECLARE @TargetTemp DECIMAL(9,3) = 1200;
DECLARE @Channel NVARCHAR(50) = 'Channel1';

SELECT TOP 1 Timestamp, FurnaceNo, ChannelDataJson
FROM dbo.FurnaceTemperatureSnapshots
WHERE FurnaceNo = 'F1'
  AND ChannelDataJson LIKE '%' + @Channel + '%'
  AND CAST(Timestamp AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY Timestamp ASC
-- Note: This is a simple string search; for proper JSON filtering, use JSON functions
```

### Query 3: Get latest snapshot per furnace
```sql
WITH RankedSnapshots AS (
	SELECT 
		*,
		ROW_NUMBER() OVER (PARTITION BY FurnaceNo ORDER BY Timestamp DESC) AS rn
	FROM dbo.FurnaceTemperatureSnapshots
	WHERE Timestamp >= DATEADD(HOUR, -1, GETDATE())
)
SELECT Id, Timestamp, FurnaceNo, ControllerId, ChannelDataJson
FROM RankedSnapshots
WHERE rn = 1
ORDER BY FurnaceNo
```

## Data Structure Reference

### FurnaceTemperatureSnapshot Class

```csharp
public sealed class FurnaceTemperatureSnapshot
{
	public long Id { get; set; }
	public DateTime Timestamp { get; set; }
	public string FurnaceNo { get; set; }
	public string ControllerId { get; set; }
	public string ChannelDataJson { get; set; }  // JSON: {"Channel1": 1200.5, ...}
	public string Source { get; set; }

	// Get channel data as dictionary
	public Dictionary<string, double> GetChannelData()
	{
		try
		{
			return JsonSerializer.Deserialize<Dictionary<string, double>>(ChannelDataJson) 
				?? new Dictionary<string, double>();
		}
		catch { return new Dictionary<string, double>(); }
	}

	// Set channel data from dictionary
	public void SetChannelData(Dictionary<string, double> data)
	{
		ChannelDataJson = JsonSerializer.Serialize(data);
	}
}
```

## Performance Tips

1. **Always use date range filters** - Avoid queries without `from` and `to` parameters
2. **Use channel filters** - When looking for specific channels, specify `channelFilter` to reduce results
3. **Limit temperature range** - Use `temperatureMin` and `temperatureMax` when possible
4. **Index awareness** - Queries are indexed on (FurnaceNo, Timestamp) for fast lookups
5. **Batch processing** - For large date ranges, process in daily chunks

## Troubleshooting

### No snapshots returned
- Verify furnace name matches exactly (case-sensitive)
- Check date range - ensure data exists in that period
- Confirm Modbus devices are online and collecting data

### JSON parsing errors
- Use try-catch when calling `GetChannelData()`
- Verify ChannelDataJson is valid JSON format
- Check for encoding issues if importing data

### Performance issues
- Add date filters to reduce dataset
- Use channel filters to narrow results
- Consider archiving old data if table grows large
