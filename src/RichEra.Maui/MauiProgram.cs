using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RichEra.Infrastructure.DependencyInjection;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		var dbPath = Path.Combine(FileSystem.AppDataDirectory, "richera_v2.db");
		builder.Services.AddRichEraInfrastructure(dbPath);

		// Auto-save backup. The singleton owns the debounce timer + state; the interceptor is
		// registered as ISaveChangesInterceptor so the DbContext (configured above) pings it
		// after every successful save.
		builder.Services.AddSingleton<RichEra.Maui.Services.AutoBackupService>();
		builder.Services.AddSingleton<RichEra.Maui.Services.ShopCartState>();
		builder.Services.AddSingleton<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor,
									  RichEra.Maui.Services.BackupAutoSaveInterceptor>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		// Kick off DB migration + seeding in the background so it never blocks app
		// startup or the first frame. The Blazor UI (MainLayout) awaits the same task
		// before rendering any data, so nothing queries the DB before it is ready.
		_ = app.Services.GetRequiredService<DatabaseInitializer>().EnsureInitializedAsync();

		return app;
	}
}
