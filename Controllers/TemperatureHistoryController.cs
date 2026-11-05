using HeatCardGenerator.Data;
using HeatCardGenerator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;

namespace HeatCardGenerator.Controllers;

[Route("temperature-history")]
public sealed class TemperatureHistoryController : Controller
{
    private readonly TemperatureHistoryStore _store;
    private readonly IWebHostEnvironment _environment;
    public TemperatureHistoryController(TemperatureHistoryStore store, IWebHostEnvironment environment) 
    { 
        _store=store; 
        _environment = environment;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string furnaceNo, [FromQuery] DateTime date, [FromQuery] double targetTemperature, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(furnaceNo)) return BadRequest("Furnace number is required.");
        if (targetTemperature < -273.15 || targetTemperature > 2500) return BadRequest("Enter a valid target temperature.");
        var from=date.Date; var to=from.AddDays(1);
        var readings = await _store.SearchAsync(furnaceNo, from, to, ct);

        if (string.Equals(furnaceNo, "DEMO-GH-3", StringComparison.OrdinalIgnoreCase) && from.Date == new DateTime(2026, 10, 28))
        {
            var demoFilePath = Path.Combine(_environment.WebRootPath, "demo-temperature-readings.csv");
            using var stream = System.IO.File.OpenRead(demoFilePath);
            var demoReadings = await TemperatureHistoryStore.ParseCsvAsync(stream, ct);
            readings.AddRange(demoReadings.Where(reading => string.Equals(reading.FurnaceNo, furnaceNo, StringComparison.OrdinalIgnoreCase)));
        }

        var results=TemperatureHistoryStore.Calculate(readings, targetTemperature);
        return Json(new { furnaceNo, date=from.ToString("yyyy-MM-dd"), targetTemperature, readingCount=readings.Count, results });
    }

    [HttpPost("import-csv")]
    [RequestSizeLimit(50_000_000)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportCsv(IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) { TempData["ImportMessage"]="Choose a CSV export first."; return RedirectToAction(nameof(Index)); }
        await using var stream=file.OpenReadStream();
        var count=await _store.ImportCsvAsync(stream, ct);
        TempData["ImportMessage"]=$"Imported {count} historical temperature readings.";
        return RedirectToAction("Index", "Home");
    }
}
