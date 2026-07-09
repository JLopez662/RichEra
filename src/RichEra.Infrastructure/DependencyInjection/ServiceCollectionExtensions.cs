using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RichEra.Application.Abstractions;
using RichEra.Infrastructure.Persistence;
using RichEra.Infrastructure.Services;

namespace RichEra.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRichEraInfrastructure(this IServiceCollection services, string sqliteFilePath)
    {
        // (sp, opts) overload so EF picks up any ISaveChangesInterceptor registered by callers
        // (e.g. the Maui auto-backup interceptor). Callers that register none behave as before.
        services.AddDbContext<AppDbContext>((sp, opts) =>
        {
            opts.UseSqlite($"Data Source={sqliteFilePath}");

            var saveInterceptors = sp.GetServices<ISaveChangesInterceptor>().ToArray();
            if (saveInterceptors.Length > 0)
                opts.AddInterceptors(saveInterceptors);
        });

        // Migration + seeding, run once off the app-startup critical path.
        services.AddSingleton<DatabaseInitializer>();

        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<ICashFlowService, CashFlowService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<IIncomeProjectionService, IncomeProjectionService>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<ISavingsService, SavingsService>();
        services.AddScoped<IItemImageService, ItemImageService>();
        services.AddScoped<IBundleService, BundleService>();
        services.AddScoped<IBalanceSnapshotService, BalanceSnapshotService>();

        services.AddScoped<IOfxParser, OfxParser>();
        services.AddScoped<IStatementImportService, StatementImportService>();

        services.AddScoped<IDataPortabilityService, DataPortabilityService>();

        return services;
    }
}
