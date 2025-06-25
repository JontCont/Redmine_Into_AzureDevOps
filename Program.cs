using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using redmineApi.Models;
using redmineApi.Services;

namespace redmineApi
{
    static class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== Redmine 到 Azure DevOps 遷移工具 ===\n");

            try
            {
                // 設定配置
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();

                var appConfig = new AppConfig();
                configuration.Bind(appConfig);

                // 設定服務
                var services = new ServiceCollection();
                ConfigureServices(services, appConfig);
                var serviceProvider = services.BuildServiceProvider();

                // 取得服務
                var logger = serviceProvider.GetRequiredService<ILogger<MigrationService>>();
                var migrationService = serviceProvider.GetRequiredService<IMigrationService>();
                var redmineFactory = serviceProvider.GetRequiredService<RedmineFactory>();

                logger.LogInformation("開始執行遷移程序...");

                // 顯示選單
                await ShowMenuAsync(migrationService, redmineFactory, logger);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"程式執行錯誤: {ex.Message}");
                Console.WriteLine($"詳細錯誤: {ex}");
            }

            Console.WriteLine("\n按任意鍵結束程式...");
            Console.ReadKey();
        }

        private static void ConfigureServices(IServiceCollection services, AppConfig config)
        {
            // 註冊配置
            services.AddSingleton(config);
            services.AddSingleton(config.Redmine);
            services.AddSingleton(config.AzureDevOps);
            services.AddSingleton(config.Migration);

            // 註冊日誌
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // 註冊服務
            services.AddSingleton<RedmineFactory>();
            services.AddSingleton<AzureDevopsFactory>();
            services.AddSingleton<IMigrationTrackingService, FileMigrationTrackingService>();
            services.AddSingleton<IMigrationService, MigrationService>();
        }

        private static async Task ShowMenuAsync(IMigrationService migrationService, RedmineFactory redmineFactory, ILogger logger)
        {
            while (true)
            {
                Console.WriteLine("\n=== 選擇操作 ===");
                Console.WriteLine("1. 遷移所有 Redmine Issues");
                Console.WriteLine("2. 遷移指定數量的 Issues");
                Console.WriteLine("3. 強制重新遷移所有 Issues");
                Console.WriteLine("4. 同步狀態");
                Console.WriteLine("5. 查看遷移摘要");
                Console.WriteLine("6. 查看 Redmine Issues 列表");
                Console.WriteLine("0. 結束程式");
                Console.Write("\n請選擇操作 (0-6): ");

                var choice = Console.ReadLine();

                try
                {
                    switch (choice)
                    {
                        case "1":
                            await MigrateAllIssuesAsync(migrationService, redmineFactory, logger);
                            break;
                        case "2":
                            await MigrateLimitedIssuesAsync(migrationService, redmineFactory, logger);
                            break;
                        case "3":
                            await ForceRemigrationAsync(migrationService, redmineFactory, logger);
                            break;
                        case "4":
                            await SyncStatusAsync(migrationService, redmineFactory, logger);
                            break;
                        case "5":
                            await ShowMigrationSummaryAsync(migrationService);
                            break;
                        case "6":
                            await ShowRedmineIssuesAsync(redmineFactory);
                            break;
                        case "0":
                            return;
                        default:
                            Console.WriteLine("無效的選擇，請重新輸入。");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "執行操作時發生錯誤");
                    Console.WriteLine($"操作失敗: {ex.Message}");
                }
            }
        }

        private static async Task MigrateAllIssuesAsync(IMigrationService migrationService, RedmineFactory redmineFactory, ILogger logger)
        {
            Console.WriteLine("\n=== 遷移所有 Issues ===");
            logger.LogInformation("開始取得所有 Redmine Issues...");

            var issues = redmineFactory.GetIssues("1000"); // 取得更多的 issues
            Console.WriteLine($"找到 {issues.Count} 個 Issues");

            if (issues.Any())
            {
                await migrationService.MigrateIssuesAsync(issues, forceUpdate: false);
            }
            else
            {
                Console.WriteLine("沒有找到任何 Issues");
            }
        }

        private static async Task MigrateLimitedIssuesAsync(IMigrationService migrationService, RedmineFactory redmineFactory, ILogger logger)
        {
            Console.Write("請輸入要遷移的 Issues 數量: ");
            if (int.TryParse(Console.ReadLine(), out int limit) && limit > 0)
            {
                Console.WriteLine($"\n=== 遷移 {limit} 個 Issues ===");
                var issues = redmineFactory.GetIssues(limit.ToString());
                Console.WriteLine($"找到 {issues.Count} 個 Issues");

                if (issues.Any())
                {
                    await migrationService.MigrateIssuesAsync(issues, forceUpdate: false);
                }
                else
                {
                    Console.WriteLine("沒有找到任何 Issues");
                }
            }
            else
            {
                Console.WriteLine("無效的數量");
            }
        }

        private static async Task ForceRemigrationAsync(IMigrationService migrationService, RedmineFactory redmineFactory, ILogger logger)
        {
            Console.WriteLine("\n=== 強制重新遷移所有 Issues ===");
            Console.WriteLine("警告: 這將重新建立所有 Work Items，可能造成重複");
            Console.Write("確定要繼續嗎? (y/N): ");
            
            var confirm = Console.ReadLine();
            if (confirm?.ToLower() == "y")
            {
                var issues = redmineFactory.GetIssues("1000");
                Console.WriteLine($"找到 {issues.Count} 個 Issues");

                if (issues.Any())
                {
                    await migrationService.MigrateIssuesAsync(issues, forceUpdate: true);
                }
            }
            else
            {
                Console.WriteLine("操作已取消");
            }
        }

        private static async Task SyncStatusAsync(IMigrationService migrationService, RedmineFactory redmineFactory, ILogger logger)
        {
            Console.WriteLine("\n=== 同步狀態 ===");
            var issues = redmineFactory.GetIssues("100");
            
            var syncCount = 0;
            foreach (var issue in issues)
            {
                if (await migrationService.SyncIssueStatusAsync(issue))
                {
                    syncCount++;
                }
            }
            
            Console.WriteLine($"同步完成: {syncCount}/{issues.Count} 個 Issues");
        }

        private static async Task ShowMigrationSummaryAsync(IMigrationService migrationService)
        {
            Console.WriteLine("\n=== 遷移摘要 ===");
            var summary = await migrationService.GetMigrationSummaryAsync();
            
            Console.WriteLine($"總 Issues: {summary.TotalIssues}");
            Console.WriteLine($"已完成: {summary.CompletedIssues}");
            Console.WriteLine($"失敗: {summary.FailedIssues}");
            Console.WriteLine($"跳過: {summary.SkippedIssues}");
            Console.WriteLine($"待處理: {summary.PendingIssues}");
            Console.WriteLine($"總附件: {summary.TotalAttachments}");
            Console.WriteLine($"成功率: {summary.SuccessRate:F1}%");
            
            if (summary.LastMigrationDate.HasValue)
            {
                Console.WriteLine($"最後遷移時間: {summary.LastMigrationDate.Value:yyyy-MM-dd HH:mm:ss}");
            }
        }

        private static Task ShowRedmineIssuesAsync(RedmineFactory redmineFactory)
        {
            Console.WriteLine("\n=== Redmine Issues 列表 ===");
            var issues = redmineFactory.GetIssues("20");
            
            foreach (var issue in issues)
            {
                Console.WriteLine($"#{issue.Id} - [{issue.Tracker?.Name}] - {issue.Subject}");
                Console.WriteLine($"  狀態: {issue.Status?.Name} | 指派: {issue.AssignedTo?.Name ?? "未指派"}");
                Console.WriteLine($"  建立: {issue.CreatedOn:yyyy-MM-dd} | 附件: {issue.Attachments?.Count() ?? 0} 個");
                Console.WriteLine();
            }
            
            return Task.CompletedTask;
        }
    }
}