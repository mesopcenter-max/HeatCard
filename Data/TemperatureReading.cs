using System.Collections.Generic;
using System.Text.Json;

namespace HeatCardGenerator.Data;

public sealed class TemperatureReading
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string FurnaceNo { get; set; } = "";
    public string ControllerId { get; set; } = "";
    public string Channel { get; set; } = "";
    public double Temperature { get; set; }
    public double ActualTemp { get; set; }
    public string Source { get; set; } = "Modbus";
}

/// <summary>
/// Stores complete furnace snapshot with all channels at a specific timestamp.
/// Enables querying by furnace, date, and temperature to get accurate correlated channel data.
/// </summary>
public sealed class FurnaceTemperatureSnapshot
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string FurnaceNo { get; set; } = "";
    public string ControllerId { get; set; } = "";

    /// <summary>
    /// JSON-serialized dictionary of channel names to actual temperatures.
    /// Example: {"CH1": 850, "CH2": 851.2, "CH3": 849.8}
    /// </summary>
    public string ChannelDataJson { get; set; } = "{}";

    public string Source { get; set; } = "Modbus";

    /// <summary>
    /// Deserialize channel data from JSON.
    /// </summary>
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

    /// <summary>
    /// Serialize channel data to JSON.
    /// </summary>
    public void SetChannelData(Dictionary<string, double> data)
    {
        ChannelDataJson = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
    }
}

public sealed class HeatTimeResult
{
    public string FurnaceNo { get; set; } = "";
    public string ControllerId { get; set; } = "";
    public string Channel { get; set; } = "";
    public double TargetTemperature { get; set; }
    public DateTime? InTime { get; set; }
    public DateTime? OutTime { get; set; }
    public TimeSpan? HoldTime => InTime.HasValue && OutTime.HasValue && OutTime >= InTime ? OutTime - InTime : null;
    public string Status { get; set; } = "Not reached";
}
