using ClosedXML.Excel;
using HeatCardGenerator.Models;

namespace HeatCardGenerator.Services;

public class HeatCardExcelService
{
    private readonly IWebHostEnvironment _env;
    private readonly LoadingPlanCatalog _loadingPlans;

    public HeatCardExcelService(IWebHostEnvironment env, LoadingPlanCatalog loadingPlans)
    {
        _env = env;
        _loadingPlans = loadingPlans;
    }

    public byte[] Generate(HeatCardViewModel m)
    {
        var path = Path.Combine(_env.ContentRootPath, "Templates", "Heat Card Template.xlsx");
        using var workbook = new XLWorkbook(path);
        var ws = workbook.Worksheet("Process Record (2)");
        Set(ws, "C9", m.Project); Set(ws, "F9", m.FdNo);
        Set(ws, "C10", m.PslNo); Set(ws, "F10", m.PartName);
        Set(ws, "C11", m.CustPartName); Set(ws, "F11", m.Specification);
        Set(ws, "C12", m.BatchNo); Set(ws, "F12", m.WorkOrderNo);
        Set(ws, "C13", m.HeatNo); Set(ws, "F13", m.SerialNo); Set(ws, "C14", m.Quantity);

        for (int i = 0; i < 11; i++)
        {
            var row = 18 + i;
            Set(ws, $"E{row}", m.Checklist.ElementAtOrDefault(i) == "Satisfactory" ? "✓" : "");
            Set(ws, $"F{row}", m.Checklist.ElementAtOrDefault(i) == "Not Satisfactory" ? "✓" : "");
        }
        for (int i = 0; i < 9; i++)
        {
            var r = 32 + i;
            var p = m.Processes.ElementAtOrDefault(i) ?? new ProcessRow();
            Set(ws, $"A{r}", p.OprNo?.ToString() ?? ""); SetDate(ws, $"C{r}", p.Date);
            Set(ws, $"D{r}", p.Process); Set(ws, $"E{r}", p.FurnaceNo);
            Set(ws, $"F{r}", p.Qty); Set(ws, $"G{r}", p.Temp);
            SetTime(ws, $"H{r}", p.InTime); SetTime(ws, $"I{r}", p.OutTime);
            Set(ws, $"J{r}", p.HoldTime); Set(ws, $"K{r}", p.CoolingMethod); Set(ws, $"L{r}", p.OperatorSign);
        }
        Set(ws, "C45", m.WaterOilBefore); Set(ws, "C46", m.WaterOilAfter);
        Set(ws, "E48", m.InspectorSignature); Set(ws, "I48", m.SupervisorSignature);
        AddLoadingPlanSheet(workbook, m);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private void AddLoadingPlanSheet(XLWorkbook workbook, HeatCardViewModel m)
    {
        var selections = m.Processes.Select((p, index) => new { Row = p, Index = index })
            .Where(x => !string.IsNullOrWhiteSpace(x.Row.LoadingPlanId))
            .Select(x => new { x.Row, x.Index, Plan = _loadingPlans.Find(x.Row.LoadingPlanId) })
            .Where(x => x.Plan != null).ToList();
        if (selections.Count == 0) return;

        var ws = workbook.Worksheets.Add("Loading Plans");
        ws.Column(1).Width = 18;
        for (var column = 2; column <= 6; column++) ws.Column(column).Width = 20;
        ws.Cell("A1").Value = "LOADING PLAN(S)"; ws.Range("A1:F1").Merge();
        ws.Cell("A1").Style.Font.Bold = true; ws.Cell("A1").Style.Font.FontSize = 16;
        ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell("A2").Value = "Project"; ws.Cell("B2").Value = m.Project;
        ws.Cell("C2").Value = "FD No"; ws.Cell("D2").Value = m.FdNo;
        ws.Cell("E2").Value = "Heat No"; ws.Cell("F2").Value = m.HeatNo;
        ws.Cell("A3").Value = "Part Name"; ws.Cell("B3").Value = m.PartName;
        ws.Cell("C3").Value = "Batch No"; ws.Cell("D3").Value = m.BatchNo;
        ws.Cell("E3").Value = "Work Order"; ws.Cell("F3").Value = m.WorkOrderNo;
        int currentRow = 5;
        foreach (var item in selections)
        {
            var plan = item.Plan!;
            ws.Cell(currentRow, 1).Value = $"Operation {item.Index + 1}: {plan.Name}";
            ws.Range(currentRow, 1, currentRow, 6).Merge();
            ws.Cell(currentRow, 1).Style.Font.Bold = true; ws.Cell(currentRow, 1).Style.Font.FontSize = 12;
            ws.Cell(currentRow + 1, 1).Value = "Process"; ws.Cell(currentRow + 1, 2).Value = item.Row.Process;
            ws.Cell(currentRow + 1, 3).Value = "Furnace"; ws.Cell(currentRow + 1, 4).Value = item.Row.FurnaceNo;
            ws.Cell(currentRow + 1, 5).Value = "Qty"; ws.Cell(currentRow + 1, 6).Value = item.Row.Qty;
            var imagePath = Path.Combine(_env.WebRootPath, plan.File.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(imagePath))
            {
                using var img = File.OpenRead(imagePath);
                ws.AddPicture(img).MoveTo(ws.Cell(currentRow + 3, 1)).WithSize(900, 620);
            }
            currentRow += 38;
        }
        ws.SheetView.FreezeRows(4);
    }

    static void Set(IXLWorksheet ws, string address, string? value) => ws.Cell(address).Value = value ?? "";
    static void SetDate(IXLWorksheet ws, string address, DateTime? value)
    {
        if (value.HasValue) { ws.Cell(address).Value = value.Value; ws.Cell(address).Style.DateFormat.Format = "dd-mm-yyyy"; }
        else ws.Cell(address).Value = "";
    }
    static void SetTime(IXLWorksheet ws, string address, string? value)
    {
        if (TimeSpan.TryParse(value, out var t)) { ws.Cell(address).Value = t; ws.Cell(address).Style.DateFormat.Format = "hh:mm"; }
        else ws.Cell(address).Value = value ?? "";
    }
}
