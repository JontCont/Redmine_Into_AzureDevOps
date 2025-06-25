namespace redmineApi.Models;

public class MigrationRecord
{
    public int RedmineIssueId { get; set; }
    public int? AzureWorkItemId { get; set; }
    public string RedmineStatus { get; set; } = string.Empty;
    public string AzureStatus { get; set; } = string.Empty;
    public string WorkItemType { get; set; } = string.Empty; // 新增欄位記錄工作項目類型
    public DateTime MigratedAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public bool HasAttachments { get; set; }
    public int AttachmentsMigrated { get; set; }
    public string? ErrorMessage { get; set; }
    public MigrationStatus Status { get; set; }
}

public enum MigrationStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped
}

public class AttachmentMigrationRecord
{
    public int RedmineIssueId { get; set; }
    public int AzureWorkItemId { get; set; }
    public int RedmineAttachmentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public bool MigrationSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime MigratedAt { get; set; }
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
