namespace HeatCardGenerator.Services;

public sealed class TemperatureHistoryOptions
{
    public string DatabaseProvider { get; set; } = "SqlServer";
    public string DatabasePath { get; set; } = "App_Data/temperature-history.db";
    public string DefaultConnection { get; set; } = "Server=(localdb)\\MSSQLLocalDB;Database=HALHeatTreatmentCard;Trusted_Connection=True;TrustServerCertificate=True;";
    public int PollIntervalSeconds { get; set; } = 5;
    public int ConnectTimeoutSeconds { get; set; } = 3;
    public List<ModbusControllerOptions> Controllers { get; set; } = new();
}

public sealed class ModbusControllerOptions
{
    public string Id { get; set; } = "Controller1";
    public string FurnaceNo { get; set; } = "Furnace 1";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;
    public string RegisterType { get; set; } = "Holding"; // Holding or Input
    public int StartAddress { get; set; } = 0;
    public int RegisterCount { get; set; } = 1;
    public int[] ChannelOffsets { get; set; } = new[] { 0 };
    public string ChannelPrefix { get; set; } = "CH";
    public double Scale { get; set; } = 1;
    public double Offset { get; set; } = 0;
    public bool Signed { get; set; }
    public int PollIntervalSeconds { get; set; } = 0;
    public bool Enabled { get; set; }
    public bool UseSingleControllerRead { get; set; } = true;
    public int[] ChannelIds { get; set; } = Array.Empty<int>();
}
