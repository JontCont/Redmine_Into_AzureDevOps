using System.Text.Json;
using redmineApi.Models;

namespace redmineApi.Services;

public interface IMigrationTrackingService
{
    Task<MigrationRecord?> GetMigrationRecordAsync(int redmineIssueId);
    Task SaveMigrationRecordAsync(MigrationRecord record);
    Task<List<MigrationRecord>> GetAllMigrationRecordsAsync();
    Task<bool> IsIssueAlreadyMigratedAsync(int redmineIssueId);
    Task SaveAttachmentRecordAsync(AttachmentMigrationRecord record);
    Task<List<AttachmentMigrationRecord>> GetAttachmentRecordsAsync(int redmineIssueId);
}

public class FileMigrationTrackingService : IMigrationTrackingService
{
    private readonly string _migrationRecordsPath;
    private readonly string _attachmentRecordsPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public FileMigrationTrackingService()
    {
        _migrationRecordsPath = Path.Combine(Directory.GetCurrentDirectory(), "migration_records.json");
        _attachmentRecordsPath = Path.Combine(Directory.GetCurrentDirectory(), "attachment_records.json");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<MigrationRecord?> GetMigrationRecordAsync(int redmineIssueId)
    {
        var records = await GetAllMigrationRecordsAsync();
        return records.FirstOrDefault(r => r.RedmineIssueId == redmineIssueId);
    }

    public async Task SaveMigrationRecordAsync(MigrationRecord record)
    {
        var records = await GetAllMigrationRecordsAsync();
        var existingIndex = records.FindIndex(r => r.RedmineIssueId == record.RedmineIssueId);
        
        if (existingIndex >= 0)
        {
            records[existingIndex] = record;
        }
        else
        {
            records.Add(record);
        }

        var json = JsonSerializer.Serialize(records, _jsonOptions);
        await File.WriteAllTextAsync(_migrationRecordsPath, json);
    }

    public async Task<List<MigrationRecord>> GetAllMigrationRecordsAsync()
    {
        if (!File.Exists(_migrationRecordsPath))
        {
            return new List<MigrationRecord>();
        }

        var json = await File.ReadAllTextAsync(_migrationRecordsPath);
        return JsonSerializer.Deserialize<List<MigrationRecord>>(json, _jsonOptions) ?? new List<MigrationRecord>();
    }

    public async Task<bool> IsIssueAlreadyMigratedAsync(int redmineIssueId)
    {
        var record = await GetMigrationRecordAsync(redmineIssueId);
        return record?.Status == MigrationStatus.Completed;
    }

    public async Task SaveAttachmentRecordAsync(AttachmentMigrationRecord record)
    {
        var records = await GetAttachmentRecordsAsync();
        records.Add(record);

        var json = JsonSerializer.Serialize(records, _jsonOptions);
        await File.WriteAllTextAsync(_attachmentRecordsPath, json);
    }

    public async Task<List<AttachmentMigrationRecord>> GetAttachmentRecordsAsync(int redmineIssueId = 0)
    {
        if (!File.Exists(_attachmentRecordsPath))
        {
            return new List<AttachmentMigrationRecord>();
        }

        var json = await File.ReadAllTextAsync(_attachmentRecordsPath);
        var allRecords = JsonSerializer.Deserialize<List<AttachmentMigrationRecord>>(json, _jsonOptions) ?? new List<AttachmentMigrationRecord>();
        
        return redmineIssueId > 0 
            ? allRecords.Where(r => r.RedmineIssueId == redmineIssueId).ToList()
            : allRecords;
    }
}
