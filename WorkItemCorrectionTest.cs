using redmineApi.Models;
using redmineApi.Services;

namespace redmineApi;

/// <summary>
/// 測試工作項目類型修正和 Redmine 同步功能
/// </summary>
public class WorkItemCorrectionTest
{
    private readonly IMigrationService _migrationService;
    
    public WorkItemCorrectionTest(IMigrationService migrationService)
    {
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
    }

    /// <summary>
    /// 測試工作項目類型修正功能
    /// </summary>
    public async Task TestWorkItemTypeCorrection()
    {
        Console.WriteLine("=== 測試工作項目類型修正功能 ===");
        
        // 假設 Redmine Issue #123 已經遷移，但工作項目類型不正確
        int redmineIssueId = 123;
        string correctWorkItemType = "Bug"; // 修正為 Bug 類型
        
        var result = await _migrationService.CorrectWorkItemTypeAsync(redmineIssueId, correctWorkItemType);
        
        if (result)
        {
            Console.WriteLine($"✅ 成功修正 Redmine Issue #{redmineIssueId} 的工作項目類型");
        }
        else
        {
            Console.WriteLine($"❌ 修正 Redmine Issue #{redmineIssueId} 的工作項目類型失敗");
        }
    }

    /// <summary>
    /// 測試同步到 Redmine 功能 (僅狀態和 commit 訊息)
    /// </summary>
    public async Task TestSyncToRedmine()
    {
        Console.WriteLine("=== 測試同步到 Redmine 功能 ===");
        
        // 假設 Azure Work Item #456 需要同步回 Redmine
        int azureWorkItemId = 456;
        string commitMessage = "修正了關鍵的 bug，詳見 commit abc123";
        
        var result = await _migrationService.SyncToRedmineAsync(azureWorkItemId, commitMessage);
        
        if (result)
        {
            Console.WriteLine($"✅ 成功同步 Azure Work Item #{azureWorkItemId} 到 Redmine");
        }
        else
        {
            Console.WriteLine($"❌ 同步 Azure Work Item #{azureWorkItemId} 到 Redmine 失敗");
        }
    }

    /// <summary>
    /// 測試僅同步狀態 (不含 commit 訊息)
    /// </summary>
    public async Task TestSyncStatusOnly()
    {
        Console.WriteLine("=== 測試僅同步狀態 ===");
        
        int azureWorkItemId = 789;
        
        var result = await _migrationService.SyncToRedmineAsync(azureWorkItemId);
        
        if (result)
        {
            Console.WriteLine($"✅ 成功同步狀態到 Redmine");
        }
        else
        {
            Console.WriteLine($"❌ 同步狀態到 Redmine 失敗");
        }
    }

    /// <summary>
    /// 執行所有測試
    /// </summary>
    public async Task RunAllTests()
    {
        Console.WriteLine("開始執行工作項目修正和同步測試...\n");
        
        try
        {
            await TestWorkItemTypeCorrection();
            Console.WriteLine();
            
            await TestSyncToRedmine();
            Console.WriteLine();
            
            await TestSyncStatusOnly();
            Console.WriteLine();
            
            Console.WriteLine("所有測試執行完成");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"測試執行過程中發生錯誤: {ex.Message}");
        }
    }
}
