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
}

public class MigrationService : IMigrationService
{
    private readonly RedmineFactory _redmineFactory;
    private readonly AzureDevopsFactory _azureFactory;
    private readonly IMigrationTrackingService _trackingService;
    private readonly MigrationConfig _config;

    public MigrationService(
        RedmineFactory redmineFactory,
        AzureDevopsFactory azureFactory,
        IMigrationTrackingService trackingService,
        MigrationConfig config)
    {
        _redmineFactory = redmineFactory ?? throw new ArgumentNullException(nameof(redmineFactory));
        _azureFactory = azureFactory ?? throw new ArgumentNullException(nameof(azureFactory));
        _trackingService = trackingService ?? throw new ArgumentNullException(nameof(trackingService));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task<bool> MigrateIssueAsync(Issue redmineIssue, bool forceUpdate = false)
    {
        if (redmineIssue?.Id == null || redmineIssue.Id <= 0)
        {
            Console.WriteLine("無效的 Redmine Issue");
            return false;
        }

        var migrationRecord = new MigrationRecord
        {
            RedmineIssueId = redmineIssue.Id,
            RedmineStatus = redmineIssue.Status?.Name ?? "Unknown",
            Status = MigrationStatus.InProgress,
            MigratedAt = DateTime.UtcNow
        };

        try
        {
            // 檢查是否已經遷移過
            if (!forceUpdate && await _trackingService.IsIssueAlreadyMigratedAsync(redmineIssue.Id))
            {
                Console.WriteLine($"Redmine Issue #{redmineIssue.Id} 已經遷移過，跳過");
                migrationRecord.Status = MigrationStatus.Skipped;
                await _trackingService.SaveMigrationRecordAsync(migrationRecord);
                return true;
            }

            // 尋找現有的 Work Item
            var existingWorkItem = await _azureFactory.FindExistingWorkItemAsync(redmineIssue.Id);
            
            Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem? workItem;

            if (existingWorkItem != null)
            {
                Console.WriteLine($"找到現有的 Work Item #{existingWorkItem.Id}，更新狀態");
                var mappedStatus = _config.StatusMapping.GetValueOrDefault(redmineIssue.Status?.Name ?? "New", "New");
                workItem = await _azureFactory.UpdateWorkItemStatusAsync(existingWorkItem.Id ?? 0, mappedStatus);
                migrationRecord.AzureWorkItemId = existingWorkItem.Id;
            }
            else
            {
                Console.WriteLine($"建立新的 Work Item 從 Redmine Issue #{redmineIssue.Id}");
                workItem = await _azureFactory.CreateWorkItemFromIssueAsync(redmineIssue);
                migrationRecord.AzureWorkItemId = workItem?.Id;
            }

            if (workItem?.Id != null)
            {
                migrationRecord.AzureWorkItemId = workItem.Id.Value;
                migrationRecord.AzureStatus = workItem.Fields.TryGetValue("System.State", out var stateValue) 
                    ? stateValue?.ToString() ?? "New" 
                    : "New";

                // 遷移附件
                if (_config.EnableAttachmentMigration && redmineIssue.Attachments?.Any() == true)
                {
                    migrationRecord.HasAttachments = true;
                    var attachmentSuccess = await MigrateAttachmentsAsync(redmineIssue, workItem.Id.Value);
                    migrationRecord.AttachmentsMigrated = redmineIssue.Attachments.Count();
                }

                migrationRecord.Status = MigrationStatus.Completed;
                migrationRecord.LastSyncAt = DateTime.UtcNow;
                
                Console.WriteLine($"✅ 成功遷移 Redmine Issue #{redmineIssue.Id} → Azure Work Item #{workItem.Id}");
            }
            else
            {
                migrationRecord.Status = MigrationStatus.Failed;
                migrationRecord.ErrorMessage = "無法建立或更新 Work Item";
                Console.WriteLine($"❌ 遷移失敗: Redmine Issue #{redmineIssue.Id}");
            }
        }
        catch (Exception ex)
        {
            migrationRecord.Status = MigrationStatus.Failed;
            migrationRecord.ErrorMessage = ex.Message;
            Console.WriteLine($"❌ 遷移 Issue #{redmineIssue.Id} 時發生錯誤: {ex.Message}");
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
            Console.WriteLine($"處理批次 {(i / _config.BatchSize) + 1} ({i + 1}-{Math.Min(i + _config.BatchSize, totalCount)}/{totalCount})");

            var tasks = batch.Select(issue => MigrateIssueAsync(issue, forceUpdate));
            var results = await Task.WhenAll(tasks);
            
            successCount += results.Count(r => r);

            // 批次間稍作休息避免 API 限制
            if (i + _config.BatchSize < issues.Count)
            {
                await Task.Delay(1000);
            }
        }

        Console.WriteLine($"遷移完成: {successCount}/{totalCount} 成功");
        return successCount == totalCount;
    }

    public async Task<bool> SyncIssueStatusAsync(Issue redmineIssue)
    {
        if (redmineIssue?.Id == null || redmineIssue.Id <= 0) return false;

        var existingRecord = await _trackingService.GetMigrationRecordAsync(redmineIssue.Id);
        if (existingRecord?.AzureWorkItemId == null)
        {
            Console.WriteLine($"找不到對應的 Azure Work Item for Redmine Issue #{redmineIssue.Id}");
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

        Console.WriteLine($"附件遷移完成: {successCount}/{redmineIssue.Attachments.Count()} 成功");
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
