using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using redmineApi.Models;
using redmineApi.Services;

namespace redmineApi
{
    public static class Program
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
                await ShowMenuAsync(migrationService, redmineFactory, logger, serviceProvider);
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

        private static async Task ShowMenuAsync(IMigrationService migrationService, RedmineFactory redmineFactory, ILogger logger, IServiceProvider serviceProvider)
        {
            while (true)
            {
                Console.WriteLine("\n=== 選擇操作 ===");
                Console.WriteLine("1. 遷移所有 Redmine Issues");
                Console.WriteLine("2. 遷移指定數量的 Issues");
                Console.WriteLine("3. 強制重新遷移所有 Issues");
                Console.WriteLine("4. 同步狀態 (Redmine → Azure DevOps)");
                Console.WriteLine("5. 同步狀態 (Azure DevOps → Redmine)");
                Console.WriteLine("6. 查看遷移摘要");
                Console.WriteLine("7. 查看 Redmine Issues 列表");
                Console.WriteLine("8. 測試 Azure DevOps 連線");
                Console.WriteLine("9. 診斷 Issue 同步狀況");
                Console.WriteLine("10. 刷新 Work Item 狀態記錄");
                Console.WriteLine("11. 修復 null Azure Work Item ID");
                Console.WriteLine("12. 查看 Redmine 狀態清單");
                Console.WriteLine("0. 結束程式");
                Console.Write("\n請選擇操作 (0-12): ");

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
                            await SyncFromAzureDevOpsAsync(migrationService, logger);
                            break;
                        case "6":
                            await ShowMigrationSummaryAsync(migrationService);
                            break;
                        case "7":
                            await ShowRedmineIssuesAsync(redmineFactory);
                            break;
                        case "8":
                            await TestAzureDevOpsConnectionAsync(serviceProvider);
                            break;
                        case "9":
                            await DiagnoseIssueSyncAsync(migrationService);
                            break;
                        case "10":
                            await RefreshWorkItemStatusAsync(migrationService);
                            break;
                        case "11":
                            await RepairNullWorkItemIdsAsync(migrationService);
                            break;
                        case "12":
                            await ShowRedmineStatusListAsync(serviceProvider);
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
            Console.WriteLine("\n=== 同步狀態 (Redmine → Azure DevOps) ===");
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

        private static async Task SyncFromAzureDevOpsAsync(IMigrationService migrationService, ILogger logger)
        {
            Console.WriteLine("\n=== 從 Azure DevOps 同步狀態到 Redmine ===");
            Console.WriteLine("此功能會檢查已遷移的 Work Items 狀態變更，並同步回 Redmine");
            Console.WriteLine("只會同步狀態變更，不會修改其他欄位\n");
            
            Console.Write("確定要繼續嗎? (y/N): ");
            var confirm = Console.ReadLine();
            if (confirm?.ToLower() != "y")
            {
                Console.WriteLine("操作已取消");
                return;
            }

            try
            {
                Console.WriteLine("正在從 Azure DevOps 同步狀態變更...");
                var result = await migrationService.SyncAllToRedmineAsync();
                
                Console.WriteLine($"\n同步結果:");
                Console.WriteLine($"  成功同步: {result.SuccessCount} 項");
                Console.WriteLine($"  同步失敗: {result.FailureCount} 項");
                Console.WriteLine($"  無需更新: {result.SkippedCount} 項");
                
                if (result.FailureCount > 0)
                {
                    Console.WriteLine("\n失敗的項目:");
                    foreach (var error in result.Errors.Take(5)) // 只顯示前5個錯誤
                    {
                        Console.WriteLine($"  - Work Item #{error.WorkItemId}: {error.Message}");
                    }
                    
                    if (result.Errors.Count > 5)
                    {
                        Console.WriteLine($"  ... 還有 {result.Errors.Count - 5} 個錯誤");
                    }
                }
                
                Console.WriteLine($"\n同步完成！總處理 {result.TotalProcessed} 個 Work Items");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Azure DevOps 到 Redmine 同步失敗");
                Console.WriteLine($"同步失敗: {ex.Message}");
            }
        }

        private static async Task TestAzureDevOpsConnectionAsync(IServiceProvider serviceProvider)
        {
            Console.WriteLine("\n=== 測試 Azure DevOps 連線 ===");
            
            try
            {
                var azureFactory = serviceProvider.GetRequiredService<AzureDevopsFactory>();
                var migrationTrackingService = serviceProvider.GetRequiredService<IMigrationTrackingService>();
                
                Console.WriteLine("正在測試 Azure DevOps 連線...");
                
                // 測試取得專案
                var project = await azureFactory.GetProject();
                Console.WriteLine($"✅ 成功連接到專案: {project.Name}");
                
                // 測試取得 Work Items (只取前5個)
                Console.WriteLine("正在取得 Work Items...");
                var workItems = await azureFactory.GetWorkItems();
                Console.WriteLine($"✅ 成功取得 {workItems.Count} 個 Work Items");
                
                if (workItems.Any())
                {
                    Console.WriteLine("\n前 5 個 Work Items:");
                    foreach (var wi in workItems.Take(5))
                    {
                        var tags = wi.Fields.ContainsKey("System.Tags") ? wi.Fields["System.Tags"]?.ToString() : "";
                        var isFromRedmine = tags?.Contains("Redmine-") == true;
                        var redmineIcon = isFromRedmine ? "🔗" : "  ";
                        
                        Console.WriteLine($"  {redmineIcon} #{wi.Id} - [{wi.Fields["System.WorkItemType"]}] {wi.Fields["System.Title"]}");
                        Console.WriteLine($"     狀態: {wi.Fields["System.State"]} | 標籤: {tags}");
                    }
                }
                
                // 檢查已遷移的記錄
                var migrationRecords = await migrationTrackingService.GetAllMigrationRecordsAsync();
                var migratedCount = migrationRecords.Count(r => r.Status == MigrationStatus.Completed);
                Console.WriteLine($"\n遷移記錄統計:");
                Console.WriteLine($"  已完成遷移: {migratedCount} 個");
                Console.WriteLine($"  總記錄數: {migrationRecords.Count} 個");
                
                if (migratedCount > 0)
                {
                    Console.WriteLine("\n最近遷移的 5 個記錄:");
                    var recentMigrations = migrationRecords
                        .Where(r => r.Status == MigrationStatus.Completed)
                        .OrderByDescending(r => r.MigratedAt)
                        .Take(5);
                        
                    foreach (var record in recentMigrations)
                    {
                        Console.WriteLine($"  🔗 Redmine #{record.RedmineIssueId} → Azure DevOps #{record.AzureWorkItemId}");
                        Console.WriteLine($"     類型: {record.WorkItemType} | 遷移時間: {record.MigratedAt:yyyy-MM-dd HH:mm}");
                    }
                }
                
                Console.WriteLine("\n✅ Azure DevOps 連線測試完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Azure DevOps 連線測試失敗: {ex.Message}");
                Console.WriteLine("\n請檢查:");
                Console.WriteLine("1. Personal Access Token 是否正確且有效");
                Console.WriteLine("2. 組織 URL 是否正確");
                Console.WriteLine("3. 專案名稱是否正確");
                Console.WriteLine("4. Token 是否有足夠的權限 (Work Items: Read & Write)");
            }
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

        private static async Task DiagnoseIssueSyncAsync(IMigrationService migrationService)
        {
            Console.WriteLine("\n=== 診斷 Issue 同步狀況 ===");
            Console.Write("請輸入 Redmine Issue ID: ");
            
            if (int.TryParse(Console.ReadLine(), out int issueId))
            {
                Console.WriteLine($"\n正在診斷 Issue #{issueId}...\n");
                
                var diagnosis = await migrationService.DiagnoseIssueSyncAsync(issueId);
                Console.WriteLine(diagnosis);
            }
            else
            {
                Console.WriteLine("❌ 無效的 Issue ID");
            }
        }

        private static async Task RefreshWorkItemStatusAsync(IMigrationService migrationService)
        {
            Console.WriteLine("\n=== 刷新 Work Item 狀態記錄 ===");
            Console.Write("請輸入 Azure Work Item ID: ");
            
            if (int.TryParse(Console.ReadLine(), out int workItemId))
            {
                Console.WriteLine($"\n正在刷新 Work Item #{workItemId} 的狀態記錄...\n");
                
                var success = await migrationService.RefreshWorkItemStatusAsync(workItemId);
                if (success)
                {
                    Console.WriteLine("\n✅ 狀態記錄刷新完成！您現在可以重新嘗試同步。");
                }
                else
                {
                    Console.WriteLine("\n❌ 刷新失敗");
                }
            }
            else
            {
                Console.WriteLine("❌ 無效的 Work Item ID");
            }
        }

        private static async Task RepairNullWorkItemIdsAsync(IMigrationService migrationService)
        {
            Console.WriteLine("\n=== 修復 null Azure Work Item ID ===");
            Console.WriteLine("此功能會:");
            Console.WriteLine("1. 尋找所有 Azure Work Item ID 為 null 的記錄");
            Console.WriteLine("2. 嘗試在 Azure DevOps 中找到對應的 Work Item");
            Console.WriteLine("3. 如果找不到，嘗試重新遷移該 Issue");
            Console.Write("\n確定要繼續嗎? (y/N): ");
            
            var confirm = Console.ReadLine();
            if (confirm?.ToLower() != "y")
            {
                Console.WriteLine("操作已取消");
                return;
            }
            
            Console.WriteLine("\n開始修復程序...\n");
            
            var result = await migrationService.RepairNullWorkItemIdsAsync();
            
            Console.WriteLine($"\n✅ 修復程序完成！");
            Console.WriteLine($"成功修復 {result.SuccessfulRepairs} 個記錄");
            
            if (result.FailedRepairs > 0)
            {
                Console.WriteLine($"⚠️  {result.FailedRepairs} 個記錄修復失敗，請檢查錯誤訊息");
            }
        }

        private static async Task ShowRedmineStatusListAsync(IServiceProvider serviceProvider)
        {
            Console.WriteLine("\n=== Redmine 狀態清單 ===");
            
            var redmineFactory = serviceProvider.GetRequiredService<RedmineFactory>();
            await Task.Run(() => redmineFactory.GetAllIssueStatuses());
        }
    }
}