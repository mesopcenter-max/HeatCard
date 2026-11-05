# Heat Card Generator

ASP.NET Core 8 MVC app that uses the supplied `Heat Card Template.xlsx` as the master template. The form writes user values into the `Process Record (2)` sheet and preserves the other workbook sheets.

## Run
1. Install .NET 8 SDK.
2. Open this folder in Visual Studio / VS Code.
3. Run `dotnet restore` then `dotnet run`.
4. Open the displayed localhost URL.
5. Fill the form and click **Generate Excel**.

The generated workbook is downloaded directly from the browser.

## Eurotherm live and historical temperature data

See `README-Eurotherm-Integration.md` for the added SQLite temperature-history database, Modbus TCP polling configuration, Reviewer CSV import, history search screen, and register-address setup. Controller polling is disabled by default until real controller IPs and register mappings have been verified.
