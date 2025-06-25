using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using redmineApi.Models;
using redmineApi.Services;

namespace redmineApi;

/// <summary>
/// 批次修正工作項目類型和狀態的工具
/// </summary>
public class BatchCorrection
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== 批次工作項目修正工具 ===\n");

        var host = CreateHostBuilder(args).Build();
        var migrationService = host.Services.GetRequiredService<IMigrationService>();

        await ShowMenu(migrationService);
    }

    private static async Task ShowMenu(IMigrationService migrationService)
    {
        while (true)
        {
            Console.WriteLine("\n選擇操作:");
            Console.WriteLine("1. 修正單一 Issue 的工作項目類型");
            Console.WriteLine("2. 同步單一 Work Item 到 Redmine");
            Console.WriteLine("3. 查看遷移摘要");
            Console.WriteLine("4. 批次修正工作項目類型");
            Console.WriteLine("5. 查看狀態對應表");
            Console.WriteLine("0. 結束");
            Console.Write("請選擇 (0-5): ");

            var choice = Console.ReadLine();

            try
            {
                switch (choice)
                {
                    case "1":
                        await CorrectSingleWorkItemType(migrationService);
                        break;
                    case "2":
                        await SyncSingleToRedmine(migrationService);
                        break;
                    case "3":
                        await ShowMigrationSummary(migrationService);
                        break;
                    case "4":
                        await BatchCorrectWorkItemTypes(migrationService);
                        break;
                    case "5":
                        ShowStatusMappings();
                        break;
                    case "0":
                        return;
                    default:
                        Console.WriteLine("無效選擇，請重新輸入");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"操作失敗: {ex.Message}");
            }
        }
    }

    private static async Task CorrectSingleWorkItemType(IMigrationService migrationService)
    {
        Console.Write("請輸入 Redmine Issue ID: ");
        if (!int.TryParse(Console.ReadLine(), out int issueId))
        {
            Console.WriteLine("無效的 Issue ID");
            return;
        }

        Console.WriteLine("選擇正確的工作項目類型:");
        Console.WriteLine("1. Bug");
        Console.WriteLine("2. Feature");
        Console.WriteLine("3. Task");
        Console.Write("請選擇 (1-3): ");

        var typeChoice = Console.ReadLine();
        string workItemType = typeChoice switch
        {
            "1" => "Bug",
            "2" => "Feature",
            "3" => "Task",
            _ => "Task"
        };

        Console.WriteLine($"正在修正 Issue #{issueId} 為 {workItemType}...");
        var result = await migrationService.CorrectWorkItemTypeAsync(issueId, workItemType);

        if (result)
        {
            Console.WriteLine($"✅ 成功修正 Issue #{issueId} 的工作項目類型");
        }
        else
        {
            Console.WriteLine($"❌ 修正失敗，請檢查 Issue 是否已遷移");
        }
    }

    private static async Task SyncSingleToRedmine(IMigrationService migrationService)
    {
        Console.Write("請輸入 Azure Work Item ID: ");
        if (!int.TryParse(Console.ReadLine(), out int workItemId))
        {
            Console.WriteLine("無效的 Work Item ID");
            return;
        }

        Console.Write("請輸入 commit 訊息 (可選，直接按 Enter 跳過): ");
        var commitMessage = Console.ReadLine();

        Console.WriteLine($"正在同步 Work Item #{workItemId} 到 Redmine...");
        var result = await migrationService.SyncToRedmineAsync(workItemId, 
            string.IsNullOrWhiteSpace(commitMessage) ? null : commitMessage);

        if (result)
        {
            Console.WriteLine($"✅ 成功同步 Work Item #{workItemId} 到 Redmine");
        }
        else
        {
            Console.WriteLine($"❌ 同步失敗，請檢查 Work Item 是否存在對應的 Redmine Issue");
        }
    }

    private static async Task ShowMigrationSummary(IMigrationService migrationService)
    {
        Console.WriteLine("正在獲取遷移摘要...");
        var summary = await migrationService.GetMigrationSummaryAsync();

        Console.WriteLine("\n=== 遷移摘要 ===");
        Console.WriteLine($"總計 Issues: {summary.TotalIssues}");
        Console.WriteLine($"已完成: {summary.CompletedIssues}");
        Console.WriteLine($"失敗: {summary.FailedIssues}");
        Console.WriteLine($"跳過: {summary.SkippedIssues}");
        Console.WriteLine($"待處理: {summary.PendingIssues}");
        Console.WriteLine($"總附件數: {summary.TotalAttachments}");
        Console.WriteLine($"成功率: {summary.SuccessRate:F1}%");
        
        if (summary.LastMigrationDate.HasValue)
        {
            Console.WriteLine($"最後遷移時間: {summary.LastMigrationDate:yyyy-MM-dd HH:mm:ss}");
        }
    }

    private static async Task BatchCorrectWorkItemTypes(IMigrationService migrationService)
    {
        Console.WriteLine("批次修正工作項目類型");
        Console.Write("請輸入要修正的 Issue IDs (用逗號分隔，例如: 73596,12345,67890): ");
        var input = Console.ReadLine();
        
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine("未輸入任何 Issue ID");
            return;
        }

        var issueIds = input.Split(',')
            .Select(id => id.Trim())
            .Where(id => int.TryParse(id, out _))
            .Select(int.Parse)
            .ToList();

        if (!issueIds.Any())
        {
            Console.WriteLine("沒有有效的 Issue ID");
            return;
        }

        Console.WriteLine("選擇統一的工作項目類型:");
        Console.WriteLine("1. Bug");
        Console.WriteLine("2. Feature");
        Console.WriteLine("3. Task");
        Console.Write("請選擇 (1-3): ");

        var typeChoice = Console.ReadLine();
        string workItemType = typeChoice switch
        {
            "1" => "Bug",
            "2" => "Feature",
            "3" => "Task",
            _ => "Task"
        };

        Console.WriteLine($"正在批次修正 {issueIds.Count} 個 Issues 為 {workItemType}...");

        int successCount = 0;
        foreach (var issueId in issueIds)
        {
            try
            {
                var result = await migrationService.CorrectWorkItemTypeAsync(issueId, workItemType);
                if (result)
                {
                    Console.WriteLine($"✅ Issue #{issueId} 修正成功");
                    successCount++;
                }
                else
                {
                    Console.WriteLine($"❌ Issue #{issueId} 修正失敗");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Issue #{issueId} 發生錯誤: {ex.Message}");
            }
        }

        Console.WriteLine($"\n批次修正完成: {successCount}/{issueIds.Count} 成功");
    }

    private static void ShowStatusMappings()
    {
        Console.WriteLine("\n=== 狀態對應表 ===");
        Console.WriteLine("Redmine → Azure DevOps:");
        Console.WriteLine("新建立 → New");
        Console.WriteLine("實作中 → Active");
        Console.WriteLine("待測試 → Resolved");
        Console.WriteLine("已結束 → Closed");
        Console.WriteLine("已關閉 → Closed");
        
        Console.WriteLine("\nAzure DevOps → Redmine:");
        Console.WriteLine("New → 新建立");
        Console.WriteLine("Active → 實作中");
        Console.WriteLine("Resolved → 待測試");
        Console.WriteLine("Closed → 已結束");

        Console.WriteLine("\n=== 工作項目類型對應 ===");
        Console.WriteLine("錯誤/臭蟲 → Bug");
        Console.WriteLine("新需求 → Feature");
        Console.WriteLine("調整 → Task");
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;
                
                var appConfig = configuration.Get<AppConfig>() ?? new AppConfig();
                services.AddSingleton(appConfig);
                services.AddSingleton(appConfig.Redmine);
                services.AddSingleton(appConfig.AzureDevOps);
                services.AddSingleton(appConfig.Migration);

                services.AddScoped<RedmineFactory>();
                services.AddScoped<AzureDevopsFactory>();
                services.AddScoped<IMigrationTrackingService, FileMigrationTrackingService>();
                services.AddScoped<IMigrationService, MigrationService>();
            });
}
