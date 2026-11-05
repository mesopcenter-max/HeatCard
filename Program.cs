using HeatCardGenerator.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<LoadingPlanCatalog>();
builder.Services.AddScoped<HeatCardExcelService>();
builder.Services.AddScoped<LoadingPlanWordService>();
builder.Services.Configure<TemperatureHistoryOptions>(builder.Configuration.GetSection("TemperatureHistory"));
builder.Services.AddSingleton<TemperatureHistoryStore>();
builder.Services.AddHostedService<ModbusPollingService>();
var app = builder.Build();
app.UseStaticFiles();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();
