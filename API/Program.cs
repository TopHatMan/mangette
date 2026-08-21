using System.Reflection;
using API;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.LibraryContext;
using API.Schema.MangaContext;
using API.Schema.NotificationsContext;
using Asp.Versioning;
using Asp.Versioning.Builder;
using Asp.Versioning.Conventions;
using log4net;
using log4net.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Newtonsoft.Json.Converters;

string banner =
    "\n\n" +
    " Mangette\n" +
    $" Built at {BuildInformation.BuildAt} for {BuildInformation.Platform} version {BuildInformation.DotNetSdkVersion}\n" +
    $" branch: {ThisAssembly.Git.Branch} commit: {ThisAssembly.Git.Commit} tag: {ThisAssembly.Git.Tag}\n" +
    $" UI: http://localhost:{Mangette.Settings.ListenPort}\n\n";

XmlConfigurator.ConfigureAndWatch(new FileInfo("Log4Net.config.xml"));
ILog log = LogManager.GetLogger("Startup");
log.Info(banner);
log.Info("Logger Configured.");

log.Info("Starting up");
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
});

log.Debug("Adding API-Explorer-helpers...");
builder.Services.AddApiVersioning(option =>
    {
        option.AssumeDefaultVersionWhenUnspecified = true;
        option.DefaultApiVersion = new ApiVersion(2);
        option.ReportApiVersions = true;
        option.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new QueryStringApiVersionReader("api-version"),
            new HeaderApiVersionReader("X-Version"),
            new MediaTypeApiVersionReader("x-version"));
    })
    .AddMvc(options =>
    {
        options.Conventions.Add(new VersionByNamespaceConvention());
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";
        options.SubstituteApiVersionInUrl = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.ConfigureOptions<NamedSwaggerGenOptions>();
builder.Services.AddSwaggerGenNewtonsoftSupport().AddSwaggerGen(opt =>
{
    string xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    opt.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});

log.Debug("Adding Database-Connection...");
Directory.CreateDirectory(MangetteSettings.DataDirectory);
Directory.CreateDirectory(Path.Join(MangetteSettings.DataDirectory, "logs"));
Directory.CreateDirectory(MangetteSettings.DefaultDownloadLocation);
Directory.CreateDirectory(Mangette.Settings.TempDownloadPath);
log.InfoFormat("SQLite database: {0}", MangetteSettings.DatabasePath);
log.InfoFormat("Listening on http://*:{0}  library {1}  temp {2}",
    Mangette.Settings.ListenPort, MangetteSettings.DefaultDownloadLocation, Mangette.Settings.TempDownloadPath);

builder.Services.AddDbContext<MangaContext>(options =>
    SqliteStorage.Configure(options, SqliteStorage.MangaHistoryTable));
builder.Services.AddDbContext<NotificationsContext>(options =>
    SqliteStorage.Configure(options, SqliteStorage.NotificationsHistoryTable));
builder.Services.AddDbContext<LibraryContext>(options =>
    SqliteStorage.Configure(options, SqliteStorage.LibraryHistoryTable));
builder.Services.AddDbContext<ActionsContext>(options =>
    SqliteStorage.Configure(options, SqliteStorage.ActionsHistoryTable));

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddControllers(options =>
{
    options.AllowEmptyInputInBodyModelBinding = true;
}).AddNewtonsoftJson(opts =>
{
    opts.SerializerSettings.Converters.Add(new StringEnumConverter());
});
builder.Services.AddScoped<ILog>(_ => LogManager.GetLogger("API"));

builder.WebHost.UseUrls($"http://*:{Mangette.Settings.ListenPort}");

log.Info("Starting app...");
WebApplication app = builder.Build();

app.UseCors("AllowAll");

ApiVersionSet apiVersionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(2))
    .ReportApiVersions()
    .Build();

app.UseCors("AllowAll");

log.Debug("Adding Swagger...");
app.UseSwagger(opts =>
{
    opts.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
    opts.RouteTemplate = "swagger/{documentName}/swagger.json";
});
app.UseSwaggerUI(opts =>
{
    opts.SwaggerEndpoint("/swagger/v2/swagger.json", "v2");
});

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

log.Debug("Mapping Controllers...");
app.MapControllers()
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(2);
app.MapFallbackToFile("index.html");

if (IsOpenApiDocumentGeneration())
{
    log.Info("OpenAPI document generation — skipping host run.");
    return;
}

try //Connect to DB and apply migrations
{
    log.Debug("Applying Migrations...");
    using (IServiceScope scope = app.Services.CreateScope())
    {
        MangaContext context = scope.ServiceProvider.GetRequiredService<MangaContext>();
        await context.Database.MigrateAsync(CancellationToken.None);
        SqliteStorage.ApplyPragmas(context);

        if (!await context.FileLibraries.AnyAsync())
        {
            await context.FileLibraries.AddAsync(new(MangetteSettings.DefaultDownloadLocation, "Default FileLibrary"),
                CancellationToken.None);
            

            if(await context.Sync(CancellationToken.None, reason: "Add default library") is { success: false } contextException)
                log.ErrorFormat("Failed to save database changes: {0}", contextException.exceptionMessage);
        }
    }

    using (IServiceScope scope = app.Services.CreateScope())
    {
        NotificationsContext context = scope.ServiceProvider.GetRequiredService<NotificationsContext>();
        await context.Database.MigrateAsync(CancellationToken.None);

        int deleted = await context.Notifications.ExecuteDeleteAsync(CancellationToken.None);
        log.DebugFormat("Deleted {0} old notifications.", deleted);
        string[] emojis =
        [
            "(•‿•)", "(づ \u25d5‿\u25d5 )づ", "( \u02d8\u25bd\u02d8)っ\u2668", "=\uff3e\u25cf \u22cf \u25cf\uff3e=",
            "（ΦωΦ）", "(\u272a\u3268\u272a)", "( ﾉ･o･ )ﾉ", "（〜^\u2207^ )〜", "~(\u2267ω\u2266)~", "૮ \u00b4• ﻌ \u00b4• ა",
            "(\u02c3ᆺ\u02c2)", "(=\ud83d\udf66 \u0f1d \ud83d\udf66=)"
        ];
        await context.Notifications.AddAsync(
            new("Mangette Started", emojis[Random.Shared.Next(0, emojis.Length - 1)], NotificationUrgency.High),
            CancellationToken.None);

        if(await context.Sync(CancellationToken.None, reason: "Startup notification") is { success: false } contextException)
            log.ErrorFormat("Failed to save database changes: {0}", contextException.exceptionMessage);
    }

    using (IServiceScope scope = app.Services.CreateScope())
    {
        LibraryContext context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
        await context.Database.MigrateAsync(CancellationToken.None);

        await context.Sync(CancellationToken.None, reason: "Startup library");
    }

    using (IServiceScope scope = app.Services.CreateScope())
    {
        ActionsContext context = scope.ServiceProvider.GetRequiredService<ActionsContext>();
        await context.Database.MigrateAsync(CancellationToken.None);
        context.Actions.Add(new StartupActionRecord());

        if(await context.Sync(CancellationToken.None, reason: "Startup actions") is { success: false } contextException)
            log.ErrorFormat("Failed to save database changes: {0}", contextException.exceptionMessage);
    }
}
catch (Exception e)
{
    log.Fatal("Migrations failed!", e);
    return;
}

log.Info("Starting Mangette.");
log.InfoFormat("FlareSolverr URL: {0}", Mangette.Settings.FlareSolverrUrl);
try
{
    using HttpClient probe = new() { Timeout = TimeSpan.FromSeconds(5) };
    HttpResponseMessage flare = await probe.GetAsync(Mangette.Settings.FlareSolverrUrl);
    log.InfoFormat("FlareSolverr reachable ({0}).", (int)flare.StatusCode);
}
catch (Exception ex)
{
    log.WarnFormat(
        "FlareSolverr is not reachable at {0} ({1}). Cloudflare-protected sites will fail until you start it: docker compose up -d",
        Mangette.Settings.FlareSolverrUrl, ex.Message);
}

Mangette.ServiceProvider = app.Services;
Mangette.StartupTasks();
Mangette.AddDefaultWorkers();

log.Info("Running app.");
await app.RunAsync();

static bool IsOpenApiDocumentGeneration()
{
    string? entry = Assembly.GetEntryAssembly()?.GetName().Name;
    if (string.Equals(entry, "GetDocument.Insider", StringComparison.OrdinalIgnoreCase))
        return true;
    string process = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
    return process.Contains("GetDocument", StringComparison.OrdinalIgnoreCase);
}