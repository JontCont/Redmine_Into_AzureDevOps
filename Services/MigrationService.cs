using Microsoft.Extensions.Logging;
using redmineApi.Models;
using redmineApi.Services;
using Redmine.Net.Api.Types;

namespace redmineApi.Services;

public interface IMigrationService
{
    Task<bool> MigrateIssueAsync(Issue redmineIssue, bool forceUpdate = false);
    Task<bool> MigrateIssuesAsync(IEnumerable<Issue> redmineIssues, bool forceUpdate = false);
    Task<bool> SyncIssueStatusAsync(Issue redmineIssue);
    Task<bool> MigrateAttachmentsAsync(Issue redmineIssue, int azureWorkItemId);
    Task<MigrationSummary> GetMigrationSummaryAsync();
    // 新增方法：修正工作項目類型
    Task<bool> CorrectWorkItemTypeAsync(int redmineIssueId, string correctWorkItemType);
    // 新增方法：同步到 Redmine
    Task<bool> SyncToRedmineAsync(int azureWorkItemId, string? commitMessage = null);
    // 新增方法：批次同步到 Redmine
    Task<SyncToRedmineResult> SyncAllToRedmineAsync();
    // 新增方法：診斷特定 Issue 的同步狀況
    Task<string> DiagnoseIssueSyncAsync(int redmineIssueId);
    // 新增方法：更新特定 Work Item 的狀態記錄
    Task<bool> RefreshWorkItemStatusAsync(int azureWorkItemId);
    // 新增方法：修復 null Azure Work Item ID 的記錄
    Task<RepairResult> RepairNullWorkItemIdsAsync();
}

public class MigrationService : IMigrationService
{
    private readonly RedmineFactory _redmineFactory;
    private readonly AzureDevopsFactory _azureFactory;
    private readonly IMigrationTrackingService _trackingService;
    private readonly MigrationConfig _config;

    private readonly ILogger<MigrationService> _logger;

    public MigrationService(
        RedmineFactory redmineFactory,
        AzureDevopsFactory azureFactory,
        IMigrationTrackingService trackingService,
        MigrationConfig config,
        ILogger<MigrationService> logger)
    {
        _redmineFactory = redmineFactory ?? throw new ArgumentNullException(nameof(redmineFactory));
        _azureFactory = azureFactory ?? throw new ArgumentNullException(nameof(azureFactory));
        _trackingService = trackingService ?? throw new ArgumentNullException(nameof(trackingService));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> MigrateIssueAsync(Issue redmineIssue, bool forceUpdate = false)
    {
        if (redmineIssue?.Id == null || redmineIssue.Id <= 0)
        {
            _logger.LogWarning("無效的 Redmine Issue");
            return false;
        }

        var migrationRecord = new MigrationRecord
        {
            RedmineIssueId = redmineIssue.Id,
            RedmineStatus = redmineIssue.Status?.Name ?? "Unknown",
            WorkItemType = _config.WorkItemTypeMapping.GetValueOrDefault(redmineIssue.Tracker?.Name ?? "Task", "Task"),
            Status = MigrationStatus.InProgress,
            MigratedAt = DateTime.UtcNow
        };

        try
        {
            // 檢查是否已經遷移過
            if (!forceUpdate && await _trackingService.IsIssueAlreadyMigratedAsync(redmineIssue.Id))
            {
                _logger.LogInformation("Redmine Issue #{RedmineIssueId} 已經遷移過，跳過", redmineIssue.Id);
                migrationRecord.Status = MigrationStatus.Skipped;
                await _trackingService.SaveMigrationRecordAsync(migrationRecord);
                return true;
            }

            // 尋找現有的 Work Item
            var existingWorkItem = await _azureFactory.FindExistingWorkItemAsync(redmineIssue.Id);
            
            Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem? workItem;

            if (existingWorkItem != null)
            {
                _logger.LogInformation("找到現有的 Work Item #{WorkItemId}，更新狀態", existingWorkItem.Id);
                var mappedStatus = _config.StatusMapping.GetValueOrDefault(redmineIssue.Status?.Name ?? "New", "New");
                workItem = await _azureFactory.UpdateWorkItemStatusAsync(existingWorkItem.Id ?? 0, mappedStatus);
                migrationRecord.AzureWorkItemId = existingWorkItem.Id;
            }
            else
            {
                _logger.LogInformation("建立新的 Work Item 從 Redmine Issue #{RedmineIssueId}", redmineIssue.Id);
                workItem = await _azureFactory.CreateWorkItemFromIssueAsync(redmineIssue);
                migrationRecord.AzureWorkItemId = workItem?.Id;
            }

            if (workItem?.Id != null)
            {
                migrationRecord.AzureWorkItemId = workItem.Id.Value;
                migrationRecord.AzureStatus = workItem.Fields.TryGetValue("System.State", out var stateValue) 
                    ? stateValue?.ToString() ?? "New" 
                    : "New";
                migrationRecord.WorkItemType = workItem.Fields.TryGetValue("System.WorkItemType", out var typeValue)
                    ? typeValue?.ToString() ?? "Task"
                    : "Task";

                // 遷移附件
                if (_config.EnableAttachmentMigration && redmineIssue.Attachments?.Any() == true)
                {
                    migrationRecord.HasAttachments = true;
                    var attachmentSuccess = await MigrateAttachmentsAsync(redmineIssue, workItem.Id.Value);
                    migrationRecord.AttachmentsMigrated = redmineIssue.Attachments.Count();
                }

                migrationRecord.Status = MigrationStatus.Completed;
                migrationRecord.LastSyncAt = DateTime.UtcNow;
                
                _logger.LogInformation("✅ 成功遷移 Redmine Issue #{RedmineIssueId} → Azure Work Item #{WorkItemId}", redmineIssue.Id, workItem.Id);
            }
            else
            {
                migrationRecord.Status = MigrationStatus.Failed;
                migrationRecord.ErrorMessage = "無法建立或更新 Work Item";
                _logger.LogError("❌ 遷移失敗: Redmine Issue #{RedmineIssueId}", redmineIssue.Id);
            }
        }
        catch (Exception ex)
        {
            migrationRecord.Status = MigrationStatus.Failed;
            migrationRecord.ErrorMessage = ex.Message;
            _logger.LogError(ex, "❌ 遷移 Issue #{RedmineIssueId} 時發生錯誤: {ErrorMessage}", redmineIssue.Id, ex.Message);
        }
        finally
        {
            await _trackingService.SaveMigrationRecordAsync(migrationRecord);
        }

        return migrationRecord.Status == MigrationStatus.Completed;
    }

    public async Task<bool> MigrateIssuesAsync(IEnumerable<Issue> redmineIssues, bool forceUpdate = false)
    {
        var issues = redmineIssues.ToList();
        Console.WriteLine($"開始遷移 {issues.Count} 個 Redmine Issues...");

        var successCount = 0;
        var totalCount = issues.Count;

        // 批次處理
        for (int i = 0; i < issues.Count; i += _config.BatchSize)
        {
            var batch = issues.Skip(i).Take(_config.BatchSize);
            _logger.LogInformation("處理批次 {Batch} ({Start}-{End}/{Total})", (i / _config.BatchSize) + 1, i + 1, Math.Min(i + _config.BatchSize, totalCount), totalCount);

            var tasks = batch.Select(issue => MigrateIssueAsync(issue, forceUpdate));
            var results = await Task.WhenAll(tasks);
            
            successCount += results.Count(r => r);

            // 批次間稍作休息避免 API 限制
            if (i + _config.BatchSize < issues.Count)
            {
                await Task.Delay(1000);
            }
        }

        _logger.LogInformation("遷移完成: {SuccessCount}/{TotalCount} 成功", successCount, totalCount);
        return successCount == totalCount;
    }

    public async Task<bool> SyncIssueStatusAsync(Issue redmineIssue)
    {
        if (redmineIssue?.Id == null || redmineIssue.Id <= 0) return false;

        var existingRecord = await _trackingService.GetMigrationRecordAsync(redmineIssue.Id);
        if (existingRecord?.AzureWorkItemId == null)
        {
            _logger.LogWarning("找不到對應的 Azure Work Item for Redmine Issue #{RedmineIssueId}", redmineIssue.Id);
            return false;
        }

        var mappedStatus = _config.StatusMapping.GetValueOrDefault(redmineIssue.Status?.Name ?? "New", "New");
        var workItem = await _azureFactory.UpdateWorkItemStatusAsync(existingRecord.AzureWorkItemId.Value, mappedStatus);

        if (workItem != null)
        {
            existingRecord.RedmineStatus = redmineIssue.Status?.Name ?? "Unknown";
            existingRecord.AzureStatus = mappedStatus;
            existingRecord.LastSyncAt = DateTime.UtcNow;
            await _trackingService.SaveMigrationRecordAsync(existingRecord);
            return true;
        }

        return false;
    }

    public async Task<bool> MigrateAttachmentsAsync(Issue redmineIssue, int azureWorkItemId)
    {
        if (redmineIssue.Attachments?.Any() != true) return true;

        var successCount = 0;
        foreach (var attachment in redmineIssue.Attachments)
        {
            try
            {
                Console.WriteLine($"下載附件: {attachment.FileName}");
                var fileContent = await _redmineFactory.DownloadAttachmentAsync(attachment);
                
                if (fileContent != null)
                {
                    var uploadSuccess = await _azureFactory.UploadAttachmentAsync(azureWorkItemId, fileContent, attachment.FileName);
                    
                    var attachmentRecord = new AttachmentMigrationRecord
                    {
                        RedmineIssueId = redmineIssue.Id,
                        AzureWorkItemId = azureWorkItemId,
                        RedmineAttachmentId = attachment.Id,
                        FileName = attachment.FileName,
                        FileSize = attachment.FileSize,
                        MigrationSuccess = uploadSuccess,
                        MigratedAt = DateTime.UtcNow,
                        ErrorMessage = uploadSuccess ? null : "上傳失敗"
                    };
                    
                    await _trackingService.SaveAttachmentRecordAsync(attachmentRecord);
                    
                    if (uploadSuccess) successCount++;
                }
                else
                {
                    var attachmentRecord = new AttachmentMigrationRecord
                    {
                        RedmineIssueId = redmineIssue.Id,
                        AzureWorkItemId = azureWorkItemId,
                        RedmineAttachmentId = attachment.Id,
                        FileName = attachment.FileName,
                        FileSize = attachment.FileSize,
                        MigrationSuccess = false,
                        MigratedAt = DateTime.UtcNow,
                        ErrorMessage = "下載失敗"
                    };
                    
                    await _trackingService.SaveAttachmentRecordAsync(attachmentRecord);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"遷移附件 {attachment.FileName} 失敗: {ex.Message}");
                
                var attachmentRecord = new AttachmentMigrationRecord
                {
                    RedmineIssueId = redmineIssue.Id,
                    AzureWorkItemId = azureWorkItemId,
                    RedmineAttachmentId = attachment.Id,
                    FileName = attachment.FileName,
                    FileSize = attachment.FileSize,
                    MigrationSuccess = false,
                    MigratedAt = DateTime.UtcNow,
                    ErrorMessage = ex.Message
                };
                
                await _trackingService.SaveAttachmentRecordAsync(attachmentRecord);
            }
        }

        _logger.LogInformation("附件遷移完成: {SuccessCount}/{TotalCount} 成功", successCount, redmineIssue.Attachments.Count());
        return successCount == redmineIssue.Attachments.Count();
    }

    public async Task<MigrationSummary> GetMigrationSummaryAsync()
    {
        var records = await _trackingService.GetAllMigrationRecordsAsync();
        
        return new MigrationSummary
        {
            TotalIssues = records.Count,
            CompletedIssues = records.Count(r => r.Status == MigrationStatus.Completed),
            FailedIssues = records.Count(r => r.Status == MigrationStatus.Failed),
            SkippedIssues = records.Count(r => r.Status == MigrationStatus.Skipped),
            PendingIssues = records.Count(r => r.Status == MigrationStatus.Pending),
            TotalAttachments = records.Sum(r => r.AttachmentsMigrated),
            LastMigrationDate = records.Where(r => r.Status == MigrationStatus.Completed)
                                      .Max(r => r.MigratedAt)
        };
    }

    public async Task<bool> CorrectWorkItemTypeAsync(int redmineIssueId, string correctWorkItemType)
    {
        try
        {
            var migrationRecord = await _trackingService.GetMigrationRecordAsync(redmineIssueId);
            if (migrationRecord?.AzureWorkItemId == null)
            {
                _logger.LogWarning("找不到對應的遷移記錄: Redmine Issue #{RedmineIssueId}", redmineIssueId);
                return false;
            }

            // 修正 Azure DevOps Work Item 類型
            var updateResult = await _azureFactory.CorrectWorkItemTypeAsync(migrationRecord.AzureWorkItemId.Value, correctWorkItemType);
            
            if (updateResult != null)
            {
                // 更新遷移記錄
                migrationRecord.WorkItemType = correctWorkItemType;
                migrationRecord.LastSyncAt = DateTime.UtcNow;
                await _trackingService.SaveMigrationRecordAsync(migrationRecord);
                
                _logger.LogInformation("✅ 成功修正 Work Item #{AzureWorkItemId} 類型為: {CorrectWorkItemType}", migrationRecord.AzureWorkItemId, correctWorkItemType);
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 修正 Work Item 類型失敗: {ErrorMessage}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 同步 Azure DevOps Work Item 狀態到 Redmine （內部方法，返回詳細結果）
    /// </summary>
    private async Task<SyncResult> SyncToRedmineInternalAsync(int azureWorkItemId, string? commitMessage = null)
    {
        try
        {
            var migrationRecord = await _trackingService.GetMigrationRecordByAzureIdAsync(azureWorkItemId);
            if (migrationRecord?.RedmineIssueId == null)
            {
                Console.WriteLine($"找不到對應的 Redmine Issue for Azure Work Item #{azureWorkItemId}");
                return SyncResult.Failed;
            }

            var redmineIssueId = migrationRecord.RedmineIssueId;
            var redmineIssue = await _redmineFactory.GetIssueAsync(redmineIssueId);
            if (redmineIssue == null)
            {
                Console.WriteLine($"找不到 Redmine Issue #{redmineIssueId}");
                return SyncResult.Failed;
            }

            // 僅更新狀態 (限制同步範圍，避免覆蓋其他欄位)
            // 使用反向狀態對應將 Azure DevOps 狀態轉換為 Redmine 狀態
            var azureStatus = migrationRecord.AzureStatus;
            var redmineStatus = _config.ReverseStatusMapping.GetValueOrDefault(azureStatus, azureStatus);
            
            // 限制：允許同步的 Redmine 狀態（New 和 Active 對應的狀態）
            var allowedRedmineStatuses = new HashSet<string> { "新建立", "新建立(new)", "待確認", "實作中", "實作中（doing）" };
            if (!allowedRedmineStatuses.Contains(redmineStatus))
            {
                Console.WriteLine($"⚠️ 跳過同步 Work Item #{azureWorkItemId} → Redmine Issue #{redmineIssueId}: 狀態 '{redmineStatus}' 不在允許同步的狀態清單中");
                Console.WriteLine($"   允許同步的狀態: {string.Join(", ", allowedRedmineStatuses)}");
                Console.WriteLine($"   目前 Azure DevOps 狀態: {azureStatus} → 對應 Redmine 狀態: {redmineStatus}");
                return SyncResult.Skipped;
            }
            
            // 限制：只有被分派者是 "Conte John" 的 Issue 才能同步
            var assigneeName = redmineIssue.AssignedTo?.Name;
            if (assigneeName != "Conte John")
            {
                Console.WriteLine($"⚠️ 跳過同步 Work Item #{azureWorkItemId} → Redmine Issue #{redmineIssueId}: 被分派者 '{assigneeName}' 不是 'Conte John'");
                Console.WriteLine($"   只有被分派給 'Conte John' 的 Issue 才能進行同步");
                return SyncResult.Skipped;
            }
            
            // 檢查狀態是否相同，如果相同就跳過同步
            var currentRedmineStatus = redmineIssue.Status?.Name;
            
            // 比較狀態時要考慮可能的狀態名稱變化（例如 "新建立(new)" vs "新建立"）
            var normalizedCurrentStatus = NormalizeStatusName(currentRedmineStatus);
            var normalizedTargetStatus = NormalizeStatusName(redmineStatus);
            
            if (normalizedCurrentStatus == normalizedTargetStatus)
            {
                Console.WriteLine($"⏭️ 跳過同步 Work Item #{azureWorkItemId} → Redmine Issue #{redmineIssueId}: 狀態已相同 ({currentRedmineStatus})");
                Console.WriteLine($"    正規化比較: '{normalizedCurrentStatus}' == '{normalizedTargetStatus}'");
                
                // 如果狀態相同，不更新任何資料也不添加 commit 訊息
                return SyncResult.Skipped;
            }
            
            Console.WriteLine($"🔄 同步狀態變更: Azure DevOps Work Item #{azureWorkItemId} ({azureStatus}) → Redmine Issue #{redmineIssueId} ({currentRedmineStatus} → {redmineStatus})");
            
            var updated = await _redmineFactory.UpdateIssueStatusAsync(redmineIssue, redmineStatus, 
                $"狀態由 Azure DevOps Work Item #{azureWorkItemId} 同步更新 ({azureStatus} → {redmineStatus})");
            
            // 如果有 commit 訊息，則添加到 Redmine
            if (!string.IsNullOrEmpty(commitMessage))
            {
                await _redmineFactory.AddCommitMessageAsync(redmineIssueId, commitMessage);
            }

            if (updated)
            {
                migrationRecord.LastSyncAt = DateTime.UtcNow;
                await _trackingService.SaveMigrationRecordAsync(migrationRecord);
                Console.WriteLine($"✅ 成功同步到 Redmine Issue #{redmineIssueId} (僅狀態和 commit 訊息)");
                return SyncResult.Success;
            }
            
            return SyncResult.Failed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 同步到 Redmine 失敗: {ex.Message}");
            return SyncResult.Failed;
        }
    }

    public async Task<bool> SyncToRedmineAsync(int azureWorkItemId, string? commitMessage = null)
    {
        var result = await SyncToRedmineInternalAsync(azureWorkItemId, commitMessage);
        return result != SyncResult.Failed;
    }

    /// <summary>
    /// 批次同步所有已遷移的 Work Items 狀態到 Redmine
    /// </summary>
    public async Task<SyncToRedmineResult> SyncAllToRedmineAsync()
    {
        Console.WriteLine("開始批次同步 Azure DevOps Work Items 狀態到 Redmine");
        
        var result = new SyncToRedmineResult();
        
        try
        {
            // 取得所有已完成遷移的記錄
            var migrationRecords = await _trackingService.GetAllMigrationRecordsAsync();
            var completedMigrations = migrationRecords
                .Where(r => r.Status == MigrationStatus.Completed && r.AzureWorkItemId.HasValue)
                .ToList();
            
            result.TotalProcessed = completedMigrations.Count;
            Console.WriteLine($"找到 {result.TotalProcessed} 個已遷移的 Work Items");
            
            foreach (var record in completedMigrations)
            {
                try
                {
                    Console.WriteLine($"檢查同步需求: Work Item #{record.AzureWorkItemId} → Redmine Issue #{record.RedmineIssueId}");
                    
                    var syncResult = await SyncToRedmineInternalAsync(record.AzureWorkItemId!.Value);
                    
                    switch (syncResult)
                    {
                        case SyncResult.Success:
                            result.SuccessCount++;
                            Console.WriteLine($"✅ 成功同步 Work Item #{record.AzureWorkItemId}");
                            break;
                        case SyncResult.Skipped:
                            result.SkippedCount++;
                            Console.WriteLine($"⏭️ 跳過 Work Item #{record.AzureWorkItemId} (狀態已相同)");
                            break;
                        case SyncResult.Failed:
                            result.FailureCount++;
                            result.Errors.Add(new SyncError
                            {
                                WorkItemId = record.AzureWorkItemId!.Value,
                                RedmineIssueId = record.RedmineIssueId,
                                Message = "同步失敗",
                                Details = "內部同步方法返回失敗"
                            });
                            Console.WriteLine($"❌ 同步 Work Item #{record.AzureWorkItemId} 失敗");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    result.FailureCount++;
                    result.Errors.Add(new SyncError
                    {
                        WorkItemId = record.AzureWorkItemId!.Value,
                        RedmineIssueId = record.RedmineIssueId,
                        Message = ex.Message,
                        Details = ex.ToString()
                    });
                    
                    Console.WriteLine($"❌ 同步 Work Item #{record.AzureWorkItemId} 時發生例外: {ex.Message}");
                }
            }
            
            Console.WriteLine($"批次同步完成：成功 {result.SuccessCount}，失敗 {result.FailureCount}，跳過 {result.SkippedCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"批次同步過程中發生錯誤: {ex.Message}");
            throw;
        }
        
        return result;
    }

    /// <summary>
    /// 診斷特定 Redmine Issue 的同步狀況
    /// </summary>
    public async Task<string> DiagnoseIssueSyncAsync(int redmineIssueId)
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine($"🔍 診斷 Redmine Issue #{redmineIssueId} 的同步狀況");
        report.AppendLine("=" + new string('=', 50));
        
        try
        {
            // 1. 檢查遷移記錄
            var migrationRecord = await _trackingService.GetMigrationRecordAsync(redmineIssueId);
            if (migrationRecord == null)
            {
                report.AppendLine("❌ 找不到遷移記錄 - 此 Issue 可能尚未遷移到 Azure DevOps");
                return report.ToString();
            }
            
            report.AppendLine($"✅ 找到遷移記錄:");
            report.AppendLine($"   - Azure Work Item ID: {migrationRecord.AzureWorkItemId}");
            report.AppendLine($"   - 遷移狀態: {migrationRecord.Status}");
            report.AppendLine($"   - Azure 狀態: {migrationRecord.AzureStatus}");
            report.AppendLine($"   - Redmine 狀態: {migrationRecord.RedmineStatus}");
            report.AppendLine($"   - 最後同步時間: {migrationRecord.LastSyncAt}");
            
            if (!migrationRecord.AzureWorkItemId.HasValue)
            {
                report.AppendLine("❌ Azure Work Item ID 為空");
                return report.ToString();
            }
            
            // 2. 檢查 Redmine Issue 詳情
            var redmineIssue = await _redmineFactory.GetIssueAsync(redmineIssueId);
            if (redmineIssue == null)
            {
                report.AppendLine("❌ 無法讀取 Redmine Issue");
                return report.ToString();
            }
            
            report.AppendLine($"\n📋 Redmine Issue 詳情:");
            report.AppendLine($"   - 當前狀態: {redmineIssue.Status?.Name}");
            report.AppendLine($"   - 被分派者: {redmineIssue.AssignedTo?.Name ?? "未分派"}");
            report.AppendLine($"   - 追蹤器: {redmineIssue.Tracker?.Name}");
            
            // 3. 檢查 Azure DevOps 狀態對應
            var azureStatus = migrationRecord.AzureStatus;
            var redmineStatus = _config.ReverseStatusMapping.GetValueOrDefault(azureStatus, azureStatus);
            
            report.AppendLine($"\n🔄 狀態對應:");
            report.AppendLine($"   - Azure DevOps 狀態: {azureStatus}");
            report.AppendLine($"   - 對應的 Redmine 狀態: {redmineStatus}");
            
            // 4. 檢查同步限制條件
            report.AppendLine($"\n🔒 同步限制檢查:");
            
            // 檢查狀態限制
            var allowedRedmineStatuses = new HashSet<string> { "新建立", "新建立(new)", "待確認", "實作中", "實作中（doing）" };
            var statusAllowed = allowedRedmineStatuses.Contains(redmineStatus);
            report.AppendLine($"   - 狀態限制: {(statusAllowed ? "✅ 通過" : "❌ 不通過")}");
            if (!statusAllowed)
            {
                report.AppendLine($"     目標狀態 '{redmineStatus}' 不在允許清單中: [{string.Join(", ", allowedRedmineStatuses)}]");
            }
            
            // 檢查被分派者限制
            var assigneeName = redmineIssue.AssignedTo?.Name;
            var assigneeAllowed = assigneeName == "Conte John";
            report.AppendLine($"   - 被分派者限制: {(assigneeAllowed ? "✅ 通過" : "❌ 不通過")}");
            if (!assigneeAllowed)
            {
                report.AppendLine($"     被分派者 '{assigneeName}' 不是 'Conte John'");
            }
            
            // 檢查狀態是否相同
            var currentRedmineStatus = redmineIssue.Status?.Name;
            var normalizedCurrentStatus = NormalizeStatusName(currentRedmineStatus);
            var normalizedTargetStatus = NormalizeStatusName(redmineStatus);
            var statusSame = normalizedCurrentStatus == normalizedTargetStatus;
            
            report.AppendLine($"   - 狀態比較:");
            report.AppendLine($"     當前狀態: '{currentRedmineStatus}' (正規化: '{normalizedCurrentStatus}')");
            report.AppendLine($"     目標狀態: '{redmineStatus}' (正規化: '{normalizedTargetStatus}')");
            report.AppendLine($"     狀態相同: {(statusSame ? "是" : "否")}");
            
            // 5. 總結
            report.AppendLine($"\n📊 同步結論:");
            if (!statusAllowed)
            {
                report.AppendLine("❌ 無法同步 - 目標狀態不在允許清單中");
            }
            else if (!assigneeAllowed)
            {
                report.AppendLine("❌ 無法同步 - 被分派者不是 'Conte John'");
            }
            else if (statusSame)
            {
                report.AppendLine("⏭️ 無需同步 - 狀態已相同");
            }
            else
            {
                report.AppendLine("✅ 可以同步 - 所有條件都滿足");
            }
        }
        catch (Exception ex)
        {
            report.AppendLine($"❌ 診斷過程中發生錯誤: {ex.Message}");
        }
        
        return report.ToString();
    }

    /// <summary>
    /// 正規化狀態名稱，移除括號和額外的描述文字
    /// </summary>
    private string NormalizeStatusName(string? statusName)
    {
        if (string.IsNullOrWhiteSpace(statusName))
            return "";

        // 移除括號及其內容，並修正常見的狀態名稱變化
        var normalized = statusName
            .Replace("(new)", "")
            .Replace("（doing）", "")
            .Replace("(ready to test)", "")
            .Replace("(", "")
            .Replace(")", "")
            .Replace("（", "")
            .Replace("）", "")
            .Trim();

        // 處理常見的狀態名稱對應
        return normalized switch
        {
            "新建立" => "新建立",
            "實作中" => "實作中", 
            "待測試" => "待測試",
            "已結束" => "已結束",
            "已關閉" => "已結束",
            _ => normalized
        };
    }

    public async Task<bool> RefreshWorkItemStatusAsync(int azureWorkItemId)
    {
        try
        {
            Console.WriteLine($"🔄 正在刷新 Azure Work Item #{azureWorkItemId} 的狀態記錄...");
            
            // 1. 從 Azure DevOps 取得最新的 Work Item 資訊
            var workItem = await _azureFactory.GetWorkItemAsync(azureWorkItemId);
            if (workItem == null)
            {
                Console.WriteLine($"❌ 找不到 Azure Work Item #{azureWorkItemId}");
                return false;
            }
            
            // 2. 取得遷移記錄
            var migrationRecord = await _trackingService.GetMigrationRecordByAzureIdAsync(azureWorkItemId);
            if (migrationRecord == null)
            {
                Console.WriteLine($"❌ 找不到對應的遷移記錄 for Azure Work Item #{azureWorkItemId}");
                return false;
            }
            
            // 3. 更新狀態資訊
            var currentAzureStatus = workItem.Fields.TryGetValue("System.State", out var stateValue) 
                ? stateValue?.ToString() ?? "New" 
                : "New";
            var currentWorkItemType = workItem.Fields.TryGetValue("System.WorkItemType", out var typeValue)
                ? typeValue?.ToString() ?? "Task"
                : "Task";
            
            Console.WriteLine($"📊 狀態比較:");
            Console.WriteLine($"   - 記錄中的 Azure 狀態: {migrationRecord.AzureStatus}");
            Console.WriteLine($"   - 實際的 Azure 狀態: {currentAzureStatus}");
            Console.WriteLine($"   - 工作項目類型: {currentWorkItemType}");
            
            // 4. 更新記錄
            var statusChanged = migrationRecord.AzureStatus != currentAzureStatus;
            if (statusChanged || migrationRecord.Status == MigrationStatus.Failed)
            {
                migrationRecord.AzureStatus = currentAzureStatus;
                migrationRecord.WorkItemType = currentWorkItemType;
                migrationRecord.LastSyncAt = DateTime.UtcNow;
                
                // 如果遷移狀態是 Failed，改為 Completed
                if (migrationRecord.Status == MigrationStatus.Failed)
                {
                    migrationRecord.Status = MigrationStatus.Completed;
                    Console.WriteLine($"✅ 遷移狀態從 Failed 更新為 Completed");
                }
                
                await _trackingService.SaveMigrationRecordAsync(migrationRecord);
                
                if (statusChanged)
                {
                    Console.WriteLine($"✅ 狀態已更新: {migrationRecord.AzureStatus} → {currentAzureStatus}");
                }
                else
                {
                    Console.WriteLine($"✅ 記錄已刷新，狀態未變更");
                }
                
                return true;
            }
            else
            {
                Console.WriteLine($"⏭️ 狀態記錄已是最新，無需更新");
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 刷新狀態記錄失敗: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 修復 Azure Work Item ID 為 null 的遷移記錄
    /// </summary>
    public async Task<RepairResult> RepairNullWorkItemIdsAsync()
    {
        var result = new RepairResult();
        
        Console.WriteLine("🔧 開始修復 Azure Work Item ID 為 null 的遷移記錄");
        Console.WriteLine("=" + new string('=', 60));
        
        try
        {
            // 1. 取得所有記錄
            var allRecords = await _trackingService.GetAllMigrationRecordsAsync();
            var nullRecords = allRecords.Where(r => r.AzureWorkItemId == null).ToList();
            
            result.TotalNullRecords = nullRecords.Count;
            Console.WriteLine($"📊 找到 {result.TotalNullRecords} 個 Azure Work Item ID 為 null 的記錄");
            
            if (result.TotalNullRecords == 0)
            {
                Console.WriteLine("✅ 沒有需要修復的記錄");
                return result;
            }
            
            Console.WriteLine("\n開始逐一檢查和修復...\n");
            
            foreach (var record in nullRecords)
            {
                try
                {
                    Console.WriteLine($"🔍 檢查 Redmine Issue #{record.RedmineIssueId}");
                    
                    // 2. 嘗試在 Azure DevOps 中尋找對應的 Work Item
                    var existingWorkItem = await _azureFactory.FindExistingWorkItemAsync(record.RedmineIssueId);
                    
                    if (existingWorkItem != null)
                    {
                        // 找到現有的 Work Item，更新記錄
                        Console.WriteLine($"  ✅ 找到現有的 Work Item #{existingWorkItem.Id}");
                        
                        record.AzureWorkItemId = existingWorkItem.Id;
                        record.AzureStatus = existingWorkItem.Fields.TryGetValue("System.State", out var stateValue)
                            ? stateValue?.ToString() ?? "New"
                            : "New";
                        record.WorkItemType = existingWorkItem.Fields.TryGetValue("System.WorkItemType", out var typeValue)
                            ? typeValue?.ToString() ?? "Task"
                            : "Task";
                        record.Status = MigrationStatus.Completed;
                        record.LastSyncAt = DateTime.UtcNow;
                        
                        await _trackingService.SaveMigrationRecordAsync(record);
                        
                        result.SuccessfulRepairs++;
                        Console.WriteLine($"  ✅ 記錄已更新");
                    }
                    else
                    {
                        // 沒有找到現有的 Work Item，嘗試重新創建
                        Console.WriteLine($"  🔄 未找到現有 Work Item，嘗試重新遷移");
                        
                        // 取得 Redmine Issue
                        var redmineIssue = await _redmineFactory.GetIssueAsync(record.RedmineIssueId);
                        if (redmineIssue != null)
                        {
                            // 嘗試重新遷移
                            var success = await MigrateIssueAsync(redmineIssue, true); // 強制更新
                            
                            if (success)
                            {
                                result.SuccessfulRepairs++;
                                Console.WriteLine($"  ✅ 重新遷移成功");
                            }
                            else
                            {
                                result.FailedRepairs++;
                                result.Errors.Add(new RepairError
                                {
                                    RedmineIssueId = record.RedmineIssueId,
                                    Message = "重新遷移失敗",
                                    Details = "MigrateIssueAsync 返回 false"
                                });
                                Console.WriteLine($"  ❌ 重新遷移失敗");
                            }
                        }
                        else
                        {
                            result.FailedRepairs++;
                            result.Errors.Add(new RepairError
                            {
                                RedmineIssueId = record.RedmineIssueId,
                                Message = "找不到 Redmine Issue",
                                Details = $"無法從 Redmine 取得 Issue #{record.RedmineIssueId}"
                            });
                            Console.WriteLine($"  ❌ 找不到對應的 Redmine Issue");
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.FailedRepairs++;
                    result.Errors.Add(new RepairError
                    {
                        RedmineIssueId = record.RedmineIssueId,
                        Message = ex.Message,
                        Details = ex.ToString()
                    });
                    Console.WriteLine($"  ❌ 處理 Issue #{record.RedmineIssueId} 時發生錯誤: {ex.Message}");
                }
                
                // 避免 API 請求過於頻繁
                await Task.Delay(500);
            }
            
            Console.WriteLine($"\n📊 修復完成統計:");
            Console.WriteLine($"   總計需要修復: {result.TotalNullRecords}");
            Console.WriteLine($"   成功修復: {result.SuccessfulRepairs}");
            Console.WriteLine($"   修復失敗: {result.FailedRepairs}");
            Console.WriteLine($"   跳過處理: {result.SkippedRepairs}");
            
            if (result.Errors.Any())
            {
                Console.WriteLine($"\n❌ 錯誤詳情:");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"   Issue #{error.RedmineIssueId}: {error.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 修復過程中發生錯誤: {ex.Message}");
            throw;
        }
        
        return result;
    }
}

public class MigrationSummary
{
    public int TotalIssues { get; set; }
    public int CompletedIssues { get; set; }
    public int FailedIssues { get; set; }
    public int SkippedIssues { get; set; }
    public int PendingIssues { get; set; }
    public int TotalAttachments { get; set; }
    public DateTime? LastMigrationDate { get; set; }

    public double SuccessRate => TotalIssues > 0 ? (double)CompletedIssues / TotalIssues * 100 : 0;
}

public enum SyncResult
{
    Success,        // 成功同步
    Skipped,       // 跳過（狀態相同）
    Failed         // 同步失敗
}

public class SyncToRedmineResult
{
    public int TotalProcessed { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int SkippedCount { get; set; }
    public List<SyncError> Errors { get; set; } = new();
}

public class SyncError
{
    public int WorkItemId { get; set; }
    public int? RedmineIssueId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public class RepairResult
{
    public int TotalRecords { get; set; }
    public int RepairedRecords { get; set; }
    public int TotalNullRecords { get; set; }
    public int SuccessfulRepairs { get; set; }
    public int FailedRepairs { get; set; }
    public int SkippedRepairs { get; set; }
    public List<RepairError> Errors { get; set; } = new();
}

public class RepairError
{
    public int RedmineIssueId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}
