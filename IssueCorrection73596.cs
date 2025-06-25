using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using redmineApi.Models;
using redmineApi.Services;

namespace redmineApi;

/// <summary>
/// 專門處理 Redmine Issue #73596 的工作項目類型和狀態同步問題
/// </summary>
public class IssueCorrection73596
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Redmine Issue #73596 工作項目修正程式 ===");
        Console.WriteLine("原始 Redmine ID: #73596");
        Console.WriteLine("作者: 謝 安琪");
        Console.WriteLine("指派給: 子雯 陳");
        Console.WriteLine("狀態: 已結束");
        Console.WriteLine("優先權: 普通(p4)");
        Console.WriteLine("建立時間: 2024-12-20 14:13");
        Console.WriteLine("最後更新: 2025-02-12 16:37");
        Console.WriteLine();

        // 建立服務容器
        var host = CreateHostBuilder(args).Build();
        var migrationService = host.Services.GetRequiredService<IMigrationService>();

        // 執行修正操作
        await CorrectIssue73596(migrationService);

        Console.WriteLine("\n修正完成，按任意鍵結束...");
        Console.ReadKey();
    }

    private static async Task CorrectIssue73596(IMigrationService migrationService)
    {
        try
        {
            int redmineIssueId = 73596;
            
            Console.WriteLine("=== 步驟 1: 檢查目前的遷移狀態 ===");
            var summary = await migrationService.GetMigrationSummaryAsync();
            Console.WriteLine($"目前遷移狀態 - 總計: {summary.TotalIssues}, 完成: {summary.CompletedIssues}, 失敗: {summary.FailedIssues}");
            
            Console.WriteLine("\n=== 步驟 2: 修正工作項目類型 ===");
            Console.WriteLine("根據 Redmine 內容判斷，這個 Issue 可能需要修正為適當的工作項目類型");
            
            // 根據您的設定，可能的選項有: Bug, Feature, Task
            Console.WriteLine("請選擇正確的工作項目類型:");
            Console.WriteLine("1. Bug (錯誤/臭蟲)");
            Console.WriteLine("2. Feature (新需求)");
            Console.WriteLine("3. Task (調整/任務)");
            Console.Write("請輸入選擇 (1-3): ");
            
            var choice = Console.ReadLine();
            string correctWorkItemType = choice switch
            {
                "1" => "Bug",
                "2" => "Feature", 
                "3" => "Task",
                _ => "Task" // 預設為 Task
            };
            
            Console.WriteLine($"選擇的工作項目類型: {correctWorkItemType}");
            
            // 修正工作項目類型
            var correctionResult = await migrationService.CorrectWorkItemTypeAsync(redmineIssueId, correctWorkItemType);
            
            if (correctionResult)
            {
                Console.WriteLine($"✅ 成功修正 Redmine Issue #{redmineIssueId} 的工作項目類型為 {correctWorkItemType}");
            }
            else
            {
                Console.WriteLine($"❌ 修正工作項目類型失敗");
                Console.WriteLine("可能的原因:");
                Console.WriteLine("- Issue 尚未遷移到 Azure DevOps");
                Console.WriteLine("- Azure DevOps 連線問題");
                Console.WriteLine("- 工作項目類型不支援");
                return;
            }

            Console.WriteLine("\n=== 步驟 3: 同步狀態到 Redmine ===");
            Console.WriteLine("由於 Redmine 狀態是 '已結束'，我們需要確保 Azure DevOps 的狀態也正確同步");
            
            // 這裡我們需要先找到對應的 Azure Work Item ID
            // 在實際使用中，您可能需要手動提供這個 ID，或者從追蹤記錄中查找
            Console.Write("請輸入對應的 Azure Work Item ID (如果已知): ");
            var azureIdInput = Console.ReadLine();
            
            if (int.TryParse(azureIdInput, out int azureWorkItemId))
            {
                string commitMessage = $"同步 Redmine Issue #{redmineIssueId} 狀態 - 作者: 謝 安琪, 指派: 子雯 陳";
                
                var syncResult = await migrationService.SyncToRedmineAsync(azureWorkItemId, commitMessage);
                
                if (syncResult)
                {
                    Console.WriteLine($"✅ 成功同步 Azure Work Item #{azureWorkItemId} 到 Redmine Issue #{redmineIssueId}");
                }
                else
                {
                    Console.WriteLine($"❌ 同步失敗");
                }
            }
            else
            {
                Console.WriteLine("跳過狀態同步 (未提供有效的 Azure Work Item ID)");
            }

            Console.WriteLine("\n=== 步驟 4: 驗證修正結果 ===");
            var finalSummary = await migrationService.GetMigrationSummaryAsync();
            Console.WriteLine($"修正後狀態 - 總計: {finalSummary.TotalIssues}, 完成: {finalSummary.CompletedIssues}, 失敗: {finalSummary.FailedIssues}");
            Console.WriteLine($"成功率: {finalSummary.SuccessRate:F1}%");
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"修正過程中發生錯誤: {ex.Message}");
            Console.WriteLine("請檢查:");
            Console.WriteLine("1. appsettings.json 設定是否正確");
            Console.WriteLine("2. Redmine 和 Azure DevOps 連線是否正常");
            Console.WriteLine("3. API 權限是否足夠");
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
