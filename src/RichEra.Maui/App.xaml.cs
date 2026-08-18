using Microsoft.Extensions.DependencyInjection;
using RichEra.Application.Abstractions;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Maui;

public partial class App : Microsoft.Maui.Controls.Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		_services = services;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "Rich Era" };

		// Re-arm the daily reminder each time the app returns to the foreground — this rolls the
		// day over and picks up any settings change made while away (Ascending Tracker's resume hook).
		window.Resumed += OnResumed;
		return window;
	}

	private async void OnResumed(object? sender, EventArgs e)
	{
		try
		{
			await _services.GetRequiredService<DatabaseInitializer>().EnsureInitializedAsync();
			await _services.GetRequiredService<INotificationScheduler>().RescheduleAsync();
		}
		catch { /* reminders are best-effort */ }
	}
}
