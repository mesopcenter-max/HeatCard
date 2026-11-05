# Eurotherm temperature history integration

This update preserves the existing Heat Card Excel template and loading-plan functionality, and adds configurable Modbus TCP polling, a history search screen, and CSV import for recorder/Reviewer exports.

## Important: verify the configured hardware before enabling polling

The current `appsettings.json` has ten controllers enabled. Verify every IP address, unit ID, register address, channel mapping, and scaling against the installed controllers before running the collector. Only read-only Modbus function 03/04 requests should be used.

Open `appsettings.json` and edit `TemperatureHistory.Controllers` after verifying the configured controller settings.

- `Id`: unique controller identifier (multiple controllers are supported).
- `FurnaceNo`: the exact furnace name users will enter in the history search.
- `Host` / `Port`: controller or Modbus gateway IP and port (normally TCP port 502).
- `UnitId`: Modbus unit/slave ID behind the TCP endpoint.
- `RegisterType`: `Holding` uses function 03; `Input` uses function 04.
- `StartAddress`: **zero-based protocol address**. If Modbus Poll displays 40001 or 30001, do not blindly enter that number; confirm whether its address field is one-based or zero-based. Often 40001 maps to protocol address 0, but verify the device documentation and Modbus Poll configuration.
- `RegisterCount`: contiguous registers to read, from 1 to 125.
- `ChannelOffsets`: zero-based offsets within the read range, one per channel. Offsets must be less than `RegisterCount`.
- `Scale` / `Offset`: engineering conversion. For example raw 8500 with scale 0.1 becomes 850.0 °C. Confirm signedness and scaling from the actual device.
- `Signed`: true only if the register uses signed 16-bit representation.
- `ChannelPrefix`: names the channels `CH1`, `CH2`, etc. based on the offset array order.
- `Enabled`: enable only after validating connectivity and read-only addresses.
- `PollIntervalSeconds`: retained for configuration clarity; the current worker uses the global `TemperatureHistory.PollIntervalSeconds` interval.

If channel temperatures are not stored in adjacent registers, configure separate controller entries (or extend the mapping) rather than assuming contiguous registers. This collector only reads registers; it does not write to or control the furnace.

## Database

The current configuration selects SQL Server LocalDB (`HALHeatTreatmentCard`); `App_Data/temperature-history.db` is not used unless `DatabaseProvider` is changed to SQLite. The app creates the SQL database and tables on startup. Each successful poll inserts one row per controller into `FurnaceTemperatureSnapshots`, with channel values stored in `ChannelDataJson`; CSV imports continue to use `TemperatureReadings`. The global `PollIntervalSeconds` setting controls the collector interval. Poll failures are logged and retried on the next cycle.

## Historical data from Eurotherm Reviewer

Modbus normally supplies current values, not old trend history. Export the relevant Eurotherm recorder/Reviewer trend data to CSV, then open `/temperature-history` and upload it. Expected CSV columns are:

`Timestamp,FurnaceNo,Channel,Temperature,ControllerId`

The first four columns are required; `ControllerId` is optional. Use timestamps that include date and time, and use furnace names matching the search field. Import is append-only; avoid importing the same export repeatedly unless duplicate readings are acceptable. This basic importer expects comma-separated values and does not yet parse locale-specific semicolon CSV or complex quoted fields.

## Search and heat-time logic

The history screen groups results by furnace/controller/channel and includes both live snapshot channels and CSV-imported readings. `In Time` is the first stored sample at or above the target temperature in the selected day; `Out Time` is the first subsequent sample below target. Hold time is the elapsed time between those samples. If no later below-target sample exists during the selected day, the result reports that an out time was not found. Results reflect sample timestamps, so accuracy depends on logging interval and recorder sampling.

If a run starts before midnight, or if the channel was already hot at the start of the selected day, the first-above-target rule may not identify the true start of that heating cycle. For production heat-card use, confirm the intended cycle-selection rule and whether the search should include prior-day samples. The calculation is for process documentation and should be validated against the plant's approved procedure before operational use.

## Run

Use Visual Studio or the .NET 8 SDK. Restore NuGet packages, then run the web project. The default URL is `http://localhost:5002`. Open **Eurotherm Temperature History** from the main screen.

## Validation still required on site

Confirm the controller register map, unit IDs, scaling, and channel mapping against the actual installed controllers and known Modbus Poll readings before relying on collected values. CSV column mapping may also need adjustment to match the export produced by your specific Eurotherm product.
