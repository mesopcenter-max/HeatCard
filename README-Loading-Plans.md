# Heat Card Generator - Loading Plan Selection

## What was added
- A **Loading Plan** column in every row of **Heat Treatment Process - Actual**.
- Each row has an image button that opens a loading-plan picker.
- The picker includes all plans from the manifest, including PDF-derived image previews.
- Selected plan names are stored against the corresponding process row.
- The separate Word action downloads one plan at a time. If an editable `.docx` source exists, it is returned unchanged; otherwise the PNG preview is placed in a Word document and remains image-based.
- The main Excel `Process Record (2)` sheet omits the Loading Plan column.
- The Excel download can still include a separate `Loading Plans` worksheet containing selected template images and operation/process/furnace/quantity details.

## Loading-plan assets
The browser thumbnails are stored in `wwwroot/loading-plans/` and listed in `manifest.json`. PDF originals are not included in this workspace; their PNG previews are used for Word downloads.

The original application Excel template remains in use for generated heat-card reports.

## Run
Open the solution/project in Visual Studio and run the ASP.NET Core .NET 8 project as before.
