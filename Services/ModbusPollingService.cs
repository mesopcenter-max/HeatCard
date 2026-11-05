using HeatCardGenerator.Data;
using Microsoft.Extensions.Options;
using System.Buffers.Binary;
using System.Net.Sockets;

namespace HeatCardGenerator.Services;

public sealed class ModbusPollingService : BackgroundService
{
    private readonly TemperatureHistoryStore _store;
    private readonly TemperatureHistoryOptions _options;
    private readonly ILogger<ModbusPollingService> _logger;
    private ushort _transactionId;

    public ModbusPollingService(TemperatureHistoryStore store, IOptions<TemperatureHistoryOptions> options, ILogger<ModbusPollingService> logger)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

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

                    // Snapshot JSON stores actual temperatures for every channel.
                    var channelData = new Dictionary<string, double>();

                    for (var i = 0; i < controller.ChannelOffsets.Length; i++)
                    {
                        var offset = controller.ChannelOffsets[i];
                        if (offset < 0 || offset >= regs.Length) continue;

                        var raw = controller.Signed ? (double)(short)regs[offset] : regs[offset];
                        var value = raw * controller.Scale + controller.Offset;
                        var channelNumber = i + 1;
                        var channelName = $"{controller.ChannelPrefix}{channelNumber}";
                        channelData[channelName] = value / 10.0;
                    }

                    if (channelData.Count > 0)
                    {
                        var snapshot = new FurnaceTemperatureSnapshot
                        {
                            Timestamp = now,
                            FurnaceNo = controller.FurnaceNo,
                            ControllerId = controller.Id,
                            Source = "ModbusActualTemp"
                        };
                        snapshot.SetChannelData(channelData);

                        await _store.InsertSnapshotAsync(snapshot, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Modbus poll failed for controller {ControllerId} at {Host}:{Port}", controller.Id, controller.Host, controller.Port);
                }
            }

            var interval = Math.Max(0.01, _options.PollIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
        }
    }

    private async Task<ushort[]> ReadRegistersAsync(ModbusControllerOptions cfg, CancellationToken ct)
    {
        if (cfg.RegisterCount is < 1 or > 125) throw new InvalidOperationException("RegisterCount must be 1..125 for a single Modbus read.");
        if (cfg.StartAddress is < 0 or > 65535 || cfg.StartAddress + cfg.RegisterCount > 65536) throw new InvalidOperationException("Invalid Modbus register range.");

        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.ConnectTimeoutSeconds)));

        await client.ConnectAsync(cfg.Host, cfg.Port, timeout.Token);
        await using var stream = client.GetStream();

        var request = new byte[12];
        var transactionId = ++_transactionId;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), 6);
        request[6] = cfg.UnitId;
        request[7] = string.Equals(cfg.RegisterType, "Input", StringComparison.OrdinalIgnoreCase) ? (byte)4 : (byte)3;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8, 2), (ushort)cfg.StartAddress);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10, 2), (ushort)cfg.RegisterCount);

        await stream.WriteAsync(request, timeout.Token);

        var header = new byte[7];
        await ReadExactlyAsync(stream, header, timeout.Token);

        if (BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2)) != transactionId) throw new IOException("Modbus transaction ID mismatch.");
        if (BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2)) != 0) throw new IOException("Invalid Modbus protocol ID.");
        if (header[6] != cfg.UnitId) throw new IOException("Modbus unit ID mismatch.");

        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        if (length < 3 || length > 255) throw new IOException("Invalid Modbus TCP response length.");

        var body = new byte[length - 1];
        await ReadExactlyAsync(stream, body, timeout.Token);

        if ((body[0] & 0x80) != 0) throw new IOException($"Modbus exception response code {body[1]}.");
        if (body[0] != request[7]) throw new IOException("Unexpected Modbus function code.");

        var byteCount = body[1];
        if (byteCount != cfg.RegisterCount * 2 || body.Length < 2 + byteCount) throw new IOException("Modbus response contains an unexpected number of registers.");

        var result = new ushort[cfg.RegisterCount];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(2 + i * 2, 2));
        }

        return result;
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0) throw new EndOfStreamException("Modbus connection closed before response completed.");
            offset += read;
        }
    }
}

