using System.Collections.Specialized;
using Redmine.Net.Api;
using Redmine.Net.Api.Types;
using redmineApi.Models;

namespace redmineApi;

public class RedmineFactory : RedmineManager
{
    private readonly RedmineConfig _config;
    private string? Project { get; set; }

    public RedmineFactory(RedmineConfig config) : base(config.Host, config.ApiKey)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        Project = config.ProjectIdentifier;
    }

    public RedmineFactory(RedmineConfig config, string? project) : base(config.Host, config.ApiKey)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(project)) throw new ArgumentNullException(nameof(project));
        this.Project = project;
    }

    public IList<Issue> GetIssues(string limit = "10")
    {
        return this.GetObjects<Issue>(new NameValueCollection()
        {
            { "status_id", "*" },
            { "project_id", this.Project },
            { "limit", limit },
            { "include", "attachments,journals,relations" } // 包含附件和歷史記錄
        });
    }
    
    public Issue? GetIssueWithDetails(int issueId)
    {
        try
        {
            return this.GetObject<Issue>(issueId.ToString(), new NameValueCollection()
            {
                { "include", "attachments,journals,relations,children,changesets" }
            });
        }
        catch
        {
            return null;
        }
    }
    
    public IList<IssueStatus> GetStatuses()
    {
        return this.GetObjects<IssueStatus>();
    }

    /// <summary>
    /// 下載 Redmine 附件
    /// </summary>
    public async Task<byte[]?> DownloadAttachmentAsync(Redmine.Net.Api.Types.Attachment attachment)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Redmine-API-Key", _config.ApiKey);
            
            var response = await httpClient.GetAsync(attachment.ContentUrl);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"下載附件失敗: {attachment.FileName}, 錯誤: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 取得專案所有問題的總數
    /// </summary>
    public int GetIssuesCount()
    {
        var parameters = new NameValueCollection()
        {
            { "status_id", "*" },
            { "project_id", this.Project },
            { "limit", "1" }
        };
        
        var result = this.GetObjects<Issue>(parameters);
        // 使用 Redmine API 的 offset 和 limit 來計算總數
        var fullResult = this.GetObjects<Issue>(new NameValueCollection()
        {
            { "status_id", "*" },
            { "project_id", this.Project },
            { "limit", "1000" } // 較大的 limit 來取得更準確的計數
        });
        return fullResult?.Count ?? 0;
    }
    
}