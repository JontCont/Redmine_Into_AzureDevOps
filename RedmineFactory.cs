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
    /// 異步取得單一問題詳細資訊
    /// </summary>
    public async Task<Issue?> GetIssueAsync(int issueId)
    {
        return await Task.Run(() => GetIssueWithDetails(issueId));
    }

    /// <summary>
    /// 更新 Redmine Issue 狀態 (僅限狀態和註釋)
    /// </summary>
    public async Task<bool> UpdateIssueStatusAsync(Issue issue, string newStatus, string? notes = null)
    {
        try
        {
            // 由於 Redmine.Net.Api 的限制，我們需要小心處理更新
            // 為了安全起見，我們限制只能更新狀態和添加註釋
            
            var statusId = GetStatusId(newStatus);
            if (statusId == null)
            {
                Console.WriteLine($"找不到對應的狀態ID: {newStatus}");
                return false;
            }

            // 使用 NameValueCollection 來更新狀態
            var updateData = new Dictionary<string, string>
            {
                ["issue[status_id]"] = statusId.Value.ToString(),
                ["issue[notes]"] = notes ?? $"狀態由 Azure DevOps 同步更新為: {newStatus}"
            };

            await Task.Run(() => 
            {
                // 使用 HTTP PUT 請求直接更新 (因為 Redmine.Net.Api 的限制)
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("X-Redmine-API-Key", _config.ApiKey);
                
                var content = new FormUrlEncodedContent(updateData);
                var response = httpClient.PutAsync($"{_config.Host}/issues/{issue.Id}.json", content).Result;
                
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP {response.StatusCode}: {response.ReasonPhrase}");
                }
            });

            Console.WriteLine($"成功更新 Redmine Issue #{issue.Id} 狀態為: {newStatus}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"更新 Redmine Issue #{issue.Id} 狀態失敗: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 添加 commit 訊息到 Redmine Issue
    /// </summary>
    public async Task<bool> AddCommitMessageAsync(int issueId, string commitMessage)
    {
        try
        {
            var issue = GetIssueWithDetails(issueId);
            if (issue == null) return false;

            // 使用 HTTP PUT 請求添加註釋
            var updateData = new Dictionary<string, string>
            {
                ["issue[notes]"] = $"Commit 訊息: {commitMessage}"
            };

            await Task.Run(() => 
            {
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("X-Redmine-API-Key", _config.ApiKey);
                
                var content = new FormUrlEncodedContent(updateData);
                var response = httpClient.PutAsync($"{_config.Host}/issues/{issueId}.json", content).Result;
                
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP {response.StatusCode}: {response.ReasonPhrase}");
                }
            });

            Console.WriteLine($"成功添加 commit 訊息到 Redmine Issue #{issueId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"添加 commit 訊息到 Redmine Issue #{issueId} 失敗: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 根據狀態名稱取得狀態ID (需要預先建立對應表)
    /// </summary>
    private int? GetStatusId(string statusName)
    {
        // 這裡應該根據實際的 Redmine 狀態設定來對應
        // 建議從配置檔讀取或從 API 動態取得
        var statusMapping = new Dictionary<string, int>
        {
            { "New", 1 },
            { "Active", 2 },
            { "Resolved", 3 },
            { "Closed", 5 },
            { "新建", 1 },
            { "進行中", 2 },
            { "已解決", 3 },
            { "已關閉", 5 }
        };

        return statusMapping.GetValueOrDefault(statusName, 1); // 預設為 New
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