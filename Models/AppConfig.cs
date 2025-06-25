namespace redmineApi.Models;

public class AppConfig
{
    public RedmineConfig Redmine { get; set; } = new();
    public AzureDevOpsConfig AzureDevOps { get; set; } = new();
    public MigrationConfig Migration { get; set; } = new();
}

public class RedmineConfig
{
    public string Host { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ProjectIdentifier { get; set; } = string.Empty;
}

public class AzureDevOpsConfig
{
    public string OrganizationUrl { get; set; } = string.Empty;
    public string PersonalAccessToken { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
}

public class MigrationConfig
{
    public int BatchSize { get; set; } = 10;
    public bool EnableAttachmentMigration { get; set; } = true;
    public Dictionary<string, string> StatusMapping { get; set; } = new();
    public Dictionary<string, string> WorkItemTypeMapping { get; set; } = new();
    public Dictionary<string, string> ReverseStatusMapping { get; set; } = new();
}
