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
        // 根據實際的 Redmine 狀態設定來對應
        // 這裡需要對應到您的 Redmine 系統中的實際狀態 ID
        var statusMapping = new Dictionary<string, int>
        {
            // 英文狀態
            { "New", 1 },
            { "Active", 2 },
            { "Resolved", 3 },
            { "Closed", 5 },
            
            // 中文狀態 - 根據您的 appsettings.json 對應
            { "新建立", 1 },
            { "新建立(new)", 1 },
            { "待確認", 1 },
            { "已排入開發", 2 },
            { "實作中", 2 },
            { "實作中（doing）", 2 },
            { "待更新客戶端環境", 2 },
            { "待測試", 3 },
            { "待測試(ready to test)", 3 },
            { "待客戶驗收", 3 },
            { "已結束", 5 },
            { "已關閉", 5 }
        };

        var statusId = statusMapping.GetValueOrDefault(statusName);
        if (statusId == 0)
        {
            Console.WriteLine($"警告: 找不到狀態 '{statusName}' 的對應 ID，使用預設值 1 (New)");
            return 1; // 預設為 New
        }
        
        Console.WriteLine($"狀態對應: '{statusName}' → ID {statusId}");
        return statusId;
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

    /// <summary>
    /// 取得 Redmine 系統中所有可用的狀態
    /// </summary>
    public void GetAllIssueStatuses()
    {
        try
        {
            Console.WriteLine("📋 Redmine 系統中的所有狀態:");
            Console.WriteLine("=" + new string('=', 40));
            
            var statuses = this.GetObjects<IssueStatus>(new NameValueCollection());
            
            if (statuses != null && statuses.Any())
            {
                foreach (var status in statuses)
                {
                    Console.WriteLine($"ID: {status.Id,3} | 名稱: {status.Name}");
                }
            }
            else
            {
                Console.WriteLine("無法取得狀態清單");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"取得狀態清單失敗: {ex.Message}");
        }
    }
}