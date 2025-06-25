using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using redmineApi.Models;
using redmineApi.Services;

namespace redmineApi;

/// <summary>
/// 示範工作項目類型修正和 Redmine 同步功能的程式
/// </summary>
public class WorkItemCorrectionDemo
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Redmine-Azure DevOps 工作項目修正和同步示範 ===\n");

        // 建立服務容器
        var host = CreateHostBuilder(args).Build();
        var migrationService = host.Services.GetRequiredService<IMigrationService>();
        var testService = new WorkItemCorrectionTest(migrationService);

        // 顯示功能說明
        ShowFeatureExplanation();

        // 執行示範
        await RunDemoScenarios(migrationService);

        Console.WriteLine("\n示範完成，按任意鍵結束...");
        Console.ReadKey();
    }

    private static void ShowFeatureExplanation()
    {
        Console.WriteLine("本程式解決以下兩個問題:");
        Console.WriteLine("1. 已經生成 work type 在同步時候要可以修正回來");
        Console.WriteLine("   - 當工作項目類型錯誤時，可以重新修正");
        Console.WriteLine("   - 支援 Bug、Task、Feature 等類型修正");
        Console.WriteLine();
        Console.WriteLine("2. 同步給 redmine 只能修改狀態以及回寫 commit 訊息");
        Console.WriteLine("   - 限制同步範圍，避免覆蓋 Redmine 中的其他重要資料");
        Console.WriteLine("   - 僅允許狀態更新和 commit 訊息添加");
        Console.WriteLine();
        Console.WriteLine("開始示範...\n");
    }

    private static async Task RunDemoScenarios(IMigrationService migrationService)
    {
        try
        {
            // 示範 1: 工作項目類型修正
            Console.WriteLine("=== 示範 1: 工作項目類型修正 ===");
            Console.WriteLine("假設 Redmine Issue #123 被錯誤地遷移為 Task，現在要修正為 Bug");
            
            int redmineIssueId = 123;
            string correctType = "Bug";
            
            var correctionResult = await migrationService.CorrectWorkItemTypeAsync(redmineIssueId, correctType);
            Console.WriteLine($"修正結果: {(correctionResult ? "成功" : "失敗")}\n");

            // 示範 2: 同步狀態到 Redmine (含 commit 訊息)
            Console.WriteLine("=== 示範 2: 同步到 Redmine (含 commit 訊息) ===");
            Console.WriteLine("將 Azure Work Item #456 的狀態同步回 Redmine，並添加 commit 訊息");
            
            int azureWorkItemId = 456;
            string commitMessage = "修正了登入驗證問題 - commit: abc123def";
            
            var syncResult = await migrationService.SyncToRedmineAsync(azureWorkItemId, commitMessage);
            Console.WriteLine($"同步結果: {(syncResult ? "成功" : "失敗")}\n");

            // 示範 3: 僅同步狀態 (不含 commit 訊息)
            Console.WriteLine("=== 示範 3: 僅同步狀態 ===");
            Console.WriteLine("將 Azure Work Item #789 的狀態同步回 Redmine，不添加額外訊息");
            
            azureWorkItemId = 789;
            
            var statusSyncResult = await migrationService.SyncToRedmineAsync(azureWorkItemId);
            Console.WriteLine($"同步結果: {(statusSyncResult ? "成功" : "失敗")}\n");

            // 顯示遷移摘要
            var summary = await migrationService.GetMigrationSummaryAsync();
            Console.WriteLine("=== 遷移摘要 ===");
            Console.WriteLine($"總計問題: {summary.TotalIssues}");
            Console.WriteLine($"已完成: {summary.CompletedIssues}");
            Console.WriteLine($"失敗: {summary.FailedIssues}");
            Console.WriteLine($"跳過: {summary.SkippedIssues}");
            Console.WriteLine($"成功率: {summary.SuccessRate:F1}%");
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"示範過程中發生錯誤: {ex.Message}");
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;
                
                // 註冊設定
                var appConfig = configuration.Get<AppConfig>() ?? new AppConfig();
                services.AddSingleton(appConfig);
                services.AddSingleton(appConfig.Redmine);
                services.AddSingleton(appConfig.AzureDevOps);
                services.AddSingleton(appConfig.Migration);

                // 註冊服務
                services.AddScoped<RedmineFactory>();
                services.AddScoped<AzureDevopsFactory>();
                services.AddScoped<IMigrationTrackingService, FileMigrationTrackingService>();
                services.AddScoped<IMigrationService, MigrationService>();
            });
}
