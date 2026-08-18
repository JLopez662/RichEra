using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plugin.Maui.OCR;
using RichEra.Application.Abstractions;
using RichEra.Infrastructure.DependencyInjection;
using RichEra.Infrastructure.Persistence;
#if ANDROID
using Plugin.LocalNotification;
using Plugin.LocalNotification.AndroidOption;
#endif

namespace RichEra.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.UseOcr()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

#if ANDROID
		// Local notifications (Android): one high-importance channel for the daily reminder,
		// which MauiNotificationScheduler schedules from the user's preferences.
		builder.UseLocalNotification(config =>
			config.AddAndroid(android =>
				android.AddChannel(new NotificationChannelRequest
				{
					Id = RichEra.Maui.Services.MauiNotificationScheduler.ChannelId,
					Name = "Reminders",
					Description = "Daily reminder to log your transactions.",
					Importance = AndroidImportance.High,
				})));
#endif

		builder.Services.AddMauiBlazorWebView();

#if ANDROID
		// The feed's Shorts autoplay when their poster is tapped. On Android the tap lands on the
		// Blazor poster, not inside YouTube's iframe, so without this the WebView refuses to start
		// playback until a second tap. Applies to the app's single BlazorWebView.
		Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping(
			"RichEraFeedAutoplay",
			static (handler, _) =>
			{
				try
				{
					var settings = handler.PlatformView?.Settings;
					if (settings is not null) settings.MediaPlaybackRequiresUserGesture = false;
				}
				catch { /* feed nicety — never block startup */ }
			});
#endif

		var dbPath = Path.Combine(FileSystem.AppDataDirectory, "richera_v2.db");
		builder.Services.AddRichEraInfrastructure(dbPath);

		// Auto-save backup. The singleton owns the debounce timer + state; the interceptor is
		// registered as ISaveChangesInterceptor so the DbContext (configured above) pings it
		// after every successful save.
		// On-device OCR adapter for receipt scanning. UseOcr() (above) registers IOcrService;
		// this bridges it to the platform-free IReceiptScanService in Infrastructure.
		builder.Services.AddSingleton<IReceiptTextRecognizer, RichEra.Maui.Services.MauiReceiptTextRecognizer>();

		builder.Services.AddSingleton<RichEra.Maui.Services.AutoBackupService>();
		builder.Services.AddSingleton<INotificationSoundService, RichEra.Maui.Services.MauiNotificationSoundService>();
		builder.Services.AddSingleton<INotificationScheduler, RichEra.Maui.Services.MauiNotificationScheduler>();
		builder.Services.AddSingleton<RichEra.Maui.Services.ShopCartState>();

		// Money Feed — YouTube Shorts from followed finance channels and saved searches (/feed).
		// All three are singletons and none touch the EF DbContext: the catalog is pure HTTP + a
		// disk cache, and the source lists live in Preferences. That's what makes it safe for the
		// feed to fetch every source in parallel. Free: YouTube's public pages only — no API key.
		builder.Services.AddSingleton<RichEra.Maui.Services.YouTubeCatalog>();
		builder.Services.AddSingleton<IFeedSourceService, RichEra.Maui.Services.FeedSourcePreferences>();
		builder.Services.AddSingleton<IShortsFeedService, RichEra.Maui.Services.ShortsFeedService>();
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

		// Once the DB is ready, arm the daily reminder from the user's prefs (cold-start path;
		// App.Resumed re-runs it on every foreground). Fire-and-forget — non-critical to startup.
		_ = ScheduleRemindersAsync(app.Services);

		// Resolve the finance seed channels to their channel ids once, in the background, so a
		// fresh install lands on a populated feed. Best-effort and off the startup thread; the
		// seeded searches carry the feed until (and if) this completes.
		_ = Task.Run(async () =>
		{
			try { await app.Services.GetRequiredService<IFeedSourceService>().EnsureSeedChannelsAsync(); }
			catch { /* best-effort — searches still fill the feed */ }
		});

		return app;
	}

	private static async Task ScheduleRemindersAsync(IServiceProvider services)
	{
		try
		{
			await services.GetRequiredService<DatabaseInitializer>().EnsureInitializedAsync();
			await services.GetRequiredService<INotificationScheduler>().RescheduleAsync();
		}
		catch { /* reminders are best-effort */ }
	}
}
