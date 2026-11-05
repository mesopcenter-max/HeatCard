using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HeatCardGenerator.Models;

public class HeatCardViewModel
{
    public string Project { get; set; } = "";
    public string FdNo { get; set; } = "";
    public string PslNo { get; set; } = "";
    public string PartName { get; set; } = "";
    public string CustPartName { get; set; } = "";
    public string Specification { get; set; } = "";
    public string BatchNo { get; set; } = "";
    public string WorkOrderNo { get; set; } = "";
    public string HeatNo { get; set; } = "";
    public string SerialNo { get; set; } = "";
    public string Quantity { get; set; } = "";

    public string[] Checklist { get; set; } = new string[11];
    public List<ProcessRow> Processes { get; set; } = Enumerable.Range(0, 9).Select(_ => new ProcessRow()).ToList();

    public string WaterOilBefore { get; set; } = "";
    public string WaterOilAfter { get; set; } = "";
    public string InspectorSignature { get; set; } = "";
    public string SupervisorSignature { get; set; } = "";

    [BindNever]
    public IReadOnlyList<LoadingPlanOption> LoadingPlans { get; set; } = Array.Empty<LoadingPlanOption>();

    [BindNever]
    public IReadOnlyList<string> FurnaceNumbers { get; set; } = Array.Empty<string>();
}

public class ProcessRow
{
    public int? OprNo { get; set; }
    [DataType(DataType.Date)] public DateTime? Date { get; set; }
    public string Process { get; set; } = "";
    public string FurnaceNo { get; set; } = "";
    public string Qty { get; set; } = "";
    public string Temp { get; set; } = "";
    public string InTime { get; set; } = "";
    public string OutTime { get; set; } = "";
    public string HoldTime { get; set; } = "";
    public string CoolingMethod { get; set; } = "";
    public string OperatorSign { get; set; } = "";

    // Selected loading-plan image/template for this process row.
    public string LoadingPlanId { get; set; } = "";
}

public class LoadingPlanOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public string Source { get; set; } = "";
}
