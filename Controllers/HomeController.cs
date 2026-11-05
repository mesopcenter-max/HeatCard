using HeatCardGenerator.Models;
using HeatCardGenerator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HeatCardGenerator.Controllers;

public class HomeController : Controller
{
    private readonly HeatCardExcelService _excel;
    private readonly LoadingPlanWordService _word;
    private readonly LoadingPlanCatalog _loadingPlans;
    private readonly TemperatureHistoryStore _temperatureHistory;
    private readonly TemperatureHistoryOptions _temperatureHistoryOptions;

    public HomeController(HeatCardExcelService excel, LoadingPlanWordService word, LoadingPlanCatalog loadingPlans, TemperatureHistoryStore temperatureHistory, IOptions<TemperatureHistoryOptions> temperatureHistoryOptions)
    {
        _excel = excel;
        _word = word;
        _loadingPlans = loadingPlans;
        _temperatureHistory = temperatureHistory;
        _temperatureHistoryOptions = temperatureHistoryOptions.Value;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var model = new HeatCardViewModel
        {
            LoadingPlans = _loadingPlans.All,
            FurnaceNumbers = _temperatureHistoryOptions.Controllers
                .Select(controller => controller.FurnaceNo)
                .Where(furnaceNo => !string.IsNullOrWhiteSpace(furnaceNo))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(HeatCardViewModel model, CancellationToken cancellationToken)
    {
        // Only allow plans that are actually in the supplied Jigs catalog.
        foreach (var row in model.Processes)
        {
            if (!string.IsNullOrWhiteSpace(row.LoadingPlanId) &&
                _loadingPlans.Find(row.LoadingPlanId) == null)
            {
                row.LoadingPlanId = "";
            }
        }

        // Auto-fill missing times from recorded readings. Existing manually entered times are preserved.
        foreach (var row in model.Processes)
        {
            if (!row.Date.HasValue || string.IsNullOrWhiteSpace(row.FurnaceNo) ||
                !double.TryParse(row.Temp, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var target))
                continue;
            if (!string.IsNullOrWhiteSpace(row.InTime) && !string.IsNullOrWhiteSpace(row.OutTime)) continue;
            var readings = await _temperatureHistory.SearchAsync(row.FurnaceNo, row.Date.Value.Date, row.Date.Value.Date.AddDays(1), cancellationToken);
            var matches = TemperatureHistoryStore.Calculate(readings, target)
                .Where(x => x.InTime.HasValue)
                .OrderBy(x => x.InTime)
                .ToList();
            // The existing Excel template has one In/Out pair per process row, not per channel.
            // Use the earliest channel crossing and its own exit time; per-channel results are available on the history page.
            var match = matches.FirstOrDefault();
            if (match != null)
            {
                if (string.IsNullOrWhiteSpace(row.InTime)) row.InTime = match.InTime!.Value.ToString("HH:mm");
                if (string.IsNullOrWhiteSpace(row.OutTime) && match.OutTime.HasValue) row.OutTime = match.OutTime.Value.ToString("HH:mm");
                if (string.IsNullOrWhiteSpace(row.HoldTime) && match.HoldTime.HasValue) row.HoldTime = $"{(int)match.HoldTime.Value.TotalHours:D2}:{match.HoldTime.Value.Minutes:D2}";
            }
        }

        var bytes = _excel.Generate(model);
        var safeHeat = string.IsNullOrWhiteSpace(model.HeatNo) ? "HeatCard" : model.HeatNo.Replace("/", "-").Replace("\\", "-");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"HeatCard-{safeHeat}-{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DownloadLoadingPlans(HeatCardViewModel model)
    {
        var selectedPlans = model.Processes
            .Select(row => row.LoadingPlanId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(_loadingPlans.Find)
            .Where(plan => plan != null)
            .Cast<LoadingPlanOption>()
            .ToList();

        if (selectedPlans.Count == 0)
            return BadRequest("Select a loading-plan template before downloading.");
        if (selectedPlans.Count > 1)
            return BadRequest("Download one loading-plan template at a time.");

        var plan = selectedPlans[0];
        var bytes = _word.Generate(plan);
        var safePlanName = string.Concat(plan.Name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
        return File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            $"LoadingPlan-{safePlanName}-{DateTime.Now:yyyyMMddHHmmss}.docx");
    }
}
