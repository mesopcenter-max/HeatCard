using System.Buffers.Binary;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HeatCardGenerator.Models;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace HeatCardGenerator.Services;

public class LoadingPlanWordService
{
    private readonly IWebHostEnvironment _env;
    private readonly LoadingPlanCatalog _loadingPlans;

    public LoadingPlanWordService(IWebHostEnvironment env, LoadingPlanCatalog loadingPlans)
    {
        _env = env;
        _loadingPlans = loadingPlans;
    }

    public byte[] Generate(LoadingPlanOption plan)
    {
        var editablePath = _loadingPlans.GetEditableDocumentPath(plan);
        if (editablePath != null)
            return File.ReadAllBytes(editablePath);

        var imagePath = Path.GetFullPath(Path.Combine(_env.WebRootPath,
            plan.File.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        var webRoot = Path.GetFullPath(_env.WebRootPath) + Path.DirectorySeparatorChar;
        if (!imagePath.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(imagePath))
            throw new FileNotFoundException("The loading-plan preview image is missing.", imagePath);

        using var stream = new MemoryStream();
        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = word.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;
            body.Append(new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = "120", After = "120" },
                    new Justification { Val = JustificationValues.Center }),
                new Run(new RunProperties(new Bold(), new FontSize { Val = "28" }),
                    new Text(plan.Name) { Space = SpaceProcessingModeValues.Preserve })));

            var imagePart = mainPart.AddImagePart(ImagePartType.Png);
            long width;
            long height;
            using (var imageStream = File.OpenRead(imagePath))
            {
                (width, height) = GetImageSize(imageStream);
                imageStream.Position = 0;
                imagePart.FeedData(imageStream);
            }

            var relationshipId = mainPart.GetIdOfPart(imagePart);
            body.Append(new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new Run(CreateImageDrawing(relationshipId, plan.Name, width, height))));
            body.Append(new SectionProperties(
                new PageSize { Width = 16838, Height = 11906, Orient = PageOrientationValues.Landscape },
                new PageMargin { Top = 720, Bottom = 720, Left = 720, Right = 720 }));
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static (long Width, long Height) GetImageSize(Stream stream)
    {
        const long maxWidth = 9144000;
        const long maxHeight = 5486400;
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        var pixelWidth = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        var pixelHeight = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        var scale = Math.Min((double)maxWidth / pixelWidth, (double)maxHeight / pixelHeight);
        return ((long)(pixelWidth * scale), (long)(pixelHeight * scale));
    }

    private static Drawing CreateImageDrawing(string relationshipId, string name, long width, long height) =>
        new(new DW.Inline(
            new DW.Extent { Cx = width, Cy = height },
            new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
            new DW.DocProperties { Id = 1U, Name = name },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(new A.GraphicData(
                new PIC.Picture(
                    new PIC.NonVisualPictureProperties(
                        new PIC.NonVisualDrawingProperties { Id = 1U, Name = name },
                        new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(new A.Blip { Embed = relationshipId }, new A.Stretch(new A.FillRectangle())),
                    new PIC.ShapeProperties(
                        new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = width, Cy = height }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U
        });
}
