using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using redmineApi.Models;
using redmineApi.Services;

namespace redmineApi;

/// <summary>
/// 完整處理 Issue #73596: 先遷移再修正類型和狀態
/// </summary>
public class CompleteIssue73596Fix
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Redmine Issue #73596 完整修正程式 ===");
        Console.WriteLine("此程式將:");
        Console.WriteLine("1. 重新嘗試遷移 Issue #73596 到 Azure DevOps");
        Console.WriteLine("2. 修正工作項目類型");
        Console.WriteLine("3. 同步狀態");
        Console.WriteLine();

        // 建立服務容器
        var host = CreateHostBuilder(args).Build();
        var migrationService = host.Services.GetRequiredService<IMigrationService>();
        var redmineFactory = host.Services.GetRequiredService<RedmineFactory>();

        await ProcessIssue73596(migrationService, redmineFactory);

        Console.WriteLine("\n處理完成，按任意鍵結束...");
        Console.ReadKey();
    }

    private static async Task ProcessIssue73596(IMigrationService migrationService, RedmineFactory redmineFactory)
    {
        try
        {
            int redmineIssueId = 73596;
            
            Console.WriteLine("=== 步驟 1: 從 Redmine 獲取 Issue 詳細資訊 ===");
            
            // 獲取 Issue 詳細資訊
            var issue = redmineFactory.GetIssueWithDetails(redmineIssueId);
            if (issue == null)
            {
                Console.WriteLine($"❌ 無法從 Redmine 獲取 Issue #{redmineIssueId}");
                Console.WriteLine("請檢查:");
                Console.WriteLine("- Issue ID 是否正確");
                Console.WriteLine("- Redmine API 連線是否正常");
                Console.WriteLine("- API 金鑰權限是否足夠");
                return;
            }

            Console.WriteLine($"✅ 成功獲取 Issue #{redmineIssueId}");
            Console.WriteLine($"標題: {issue.Subject}");
            Console.WriteLine($"狀態: {issue.Status?.Name}");
            Console.WriteLine($"類型: {issue.Tracker?.Name}");
            Console.WriteLine($"作者: {issue.Author?.Name}");
            Console.WriteLine($"指派給: {issue.AssignedTo?.Name ?? "未指派"}");

            Console.WriteLine("\n=== 步驟 2: 重新遷移 Issue 到 Azure DevOps ===");
            
            // 強制重新遷移
            var migrationResult = await migrationService.MigrateIssueAsync(issue, forceUpdate: true);
            
            if (migrationResult)
            {
                Console.WriteLine($"✅ 成功遷移 Issue #{redmineIssueId} 到 Azure DevOps");
            }
            else
            {
                Console.WriteLine($"❌ 遷移失敗");
                Console.WriteLine("常見問題排除:");
                Console.WriteLine("- 檢查 Azure DevOps PAT 權限");
                Console.WriteLine("- 確認專案名稱正確");
                Console.WriteLine("- 檢查網路連線");
                return;
            }

            Console.WriteLine("\n=== 步驟 3: 修正工作項目類型 ===");
            
            // 根據 Issue 內容建議類型
            string suggestedType = SuggestWorkItemType(issue);
            Console.WriteLine($"根據 Issue 內容，建議類型: {suggestedType}");
            
            Console.WriteLine("請選擇正確的工作項目類型:");
            Console.WriteLine("1. Bug (錯誤/臭蟲)");
            Console.WriteLine("2. Feature (新需求)");
            Console.WriteLine("3. Task (調整/任務)");
            Console.Write($"請輸入選擇 (1-3，預設為建議類型 {suggestedType}): ");
            
            var choice = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(choice))
            {
                choice = suggestedType == "Bug" ? "1" : suggestedType == "Feature" ? "2" : "3";
            }
            
            string correctWorkItemType = choice switch
            {
                "1" => "Bug",
                "2" => "Feature",
                "3" => "Task",
                _ => "Task"
            };
            
            Console.WriteLine($"選擇的工作項目類型: {correctWorkItemType}");
            
            // 修正工作項目類型
            var correctionResult = await migrationService.CorrectWorkItemTypeAsync(redmineIssueId, correctWorkItemType);
            
            if (correctionResult)
            {
                Console.WriteLine($"✅ 成功修正工作項目類型為 {correctWorkItemType}");
            }
            else
            {
                Console.WriteLine($"❌ 修正工作項目類型失敗");
            }

            Console.WriteLine("\n=== 步驟 4: 顯示最終狀態 ===");
            var summary = await migrationService.GetMigrationSummaryAsync();
            Console.WriteLine($"遷移摘要 - 總計: {summary.TotalIssues}, 完成: {summary.CompletedIssues}, 失敗: {summary.FailedIssues}");
            Console.WriteLine($"成功率: {summary.SuccessRate:F1}%");
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"處理過程中發生錯誤: {ex.Message}");
            Console.WriteLine("\n故障排除建議:");
            Console.WriteLine("1. 檢查 appsettings.json 設定");
            Console.WriteLine("2. 確認 Redmine 和 Azure DevOps 連線");
            Console.WriteLine("3. 驗證 API 權限");
            Console.WriteLine("4. 檢查網路連線");
        }
    }

    private static string SuggestWorkItemType(Redmine.Net.Api.Types.Issue issue)
    {
        // 根據 Issue 的內容建議工作項目類型
        var subject = issue.Subject?.ToLower() ?? "";
        var description = issue.Description?.ToLower() ?? "";
        var tracker = issue.Tracker?.Name?.ToLower() ?? "";

        // 檢查是否包含錯誤相關關鍵字
        var bugKeywords = new[] { "錯誤", "bug", "錯", "失敗", "問題", "異常", "故障" };
        if (bugKeywords.Any(keyword => subject.Contains(keyword) || description.Contains(keyword) || tracker.Contains(keyword)))
        {
            return "Bug";
        }

        // 檢查是否包含新功能相關關鍵字
        var featureKeywords = new[] { "新增", "功能", "需求", "feature", "enhancement", "新" };
        if (featureKeywords.Any(keyword => subject.Contains(keyword) || description.Contains(keyword) || tracker.Contains(keyword)))
        {
            return "Feature";
        }

        // 預設為 Task
        return "Task";
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
