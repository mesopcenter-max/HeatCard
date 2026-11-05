using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HeatCardGenerator.Models;

namespace HeatCardGenerator.Services;

public class LoadingPlanCatalog
{
    private readonly IWebHostEnvironment _env;
    private IReadOnlyList<LoadingPlanOption>? _cache;
    private IReadOnlyList<LoadingPlanOption>? _editableCache;

    public LoadingPlanCatalog(IWebHostEnvironment env) => _env = env;

    public IReadOnlyList<LoadingPlanOption> All
    {
        get
        {
            if (_cache != null) return _cache;
            var manifest = _env.WebRootFileProvider.GetFileInfo("loading-plans/manifest.json");
            if (!manifest.Exists)
                return _cache = Array.Empty<LoadingPlanOption>();

            using var stream = manifest.CreateReadStream();
            return _cache = JsonSerializer.Deserialize<List<LoadingPlanOption>>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<LoadingPlanOption>();
        }
    }

    public IReadOnlyList<LoadingPlanOption> Editable
    {
        get
        {
            if (_editableCache != null) return _editableCache;
            return _editableCache = All.Where(IsEditableWordDocument).ToList();
        }
    }

    public LoadingPlanOption? Find(string? id) =>
        All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    public LoadingPlanOption? FindEditable(string? id) =>
        Editable.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    public string? GetEditableDocumentPath(LoadingPlanOption plan)
    {
        var sourcePath = GetSourcePath(plan.Source);
        return sourcePath != null && IsEditableWordDocument(plan) ? sourcePath : null;
    }

    private bool IsEditableWordDocument(LoadingPlanOption plan)
    {
        if (!string.Equals(Path.GetExtension(plan.Source), ".docx", StringComparison.OrdinalIgnoreCase))
            return false;

        var path = GetSourcePath(plan.Source);
        if (path == null || !File.Exists(path))
            return false;

        try
        {
            using var document = WordprocessingDocument.Open(path, false);
            return document.MainDocumentPart?.Document?.Body?.Descendants<Text>()
                .Any(text => !string.IsNullOrWhiteSpace(text.Text)) == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string? GetSourcePath(string source)
    {
        var root = Path.GetFullPath(_env.ContentRootPath) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : null;
    }
}
