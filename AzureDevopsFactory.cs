using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using Newtonsoft.Json;
using redmineApi.Models;
using Redmine.Net.Api.Types;

namespace redmineApi;

public class AzureDevopsFactory : VssConnection
{
    private readonly AzureDevOpsConfig _config;
    private readonly MigrationConfig _migrationConfig;
    private readonly VssConnection _connection;

    public AzureDevopsFactory(AzureDevOpsConfig config, MigrationConfig migrationConfig) 
        : base(new Uri(config.OrganizationUrl), new VssBasicCredential(string.Empty, config.PersonalAccessToken))
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _migrationConfig = migrationConfig ?? throw new ArgumentNullException(nameof(migrationConfig));
        _connection = new VssConnection(new Uri(config.OrganizationUrl), 
            new VssBasicCredential(string.Empty, config.PersonalAccessToken));
    }

    public async Task<TeamProjectReference> GetProject()
    {
        var projectClient = _connection!.GetClient<ProjectHttpClient>();
        var project = await projectClient.GetProject(_config.ProjectName).ConfigureAwait(false);
        return project;
    }

    public async Task<List<WorkItem>> GetWorkItems()
    {
        using var workItemClient = _connection.GetClient<WorkItemTrackingHttpClient>();

        var wiql = new Wiql
        {
            Query = $@"Select [System.Id] From WorkItems"
        };

        var workItemsIds = await workItemClient.QueryByWiqlAsync(wiql, _config.ProjectName).ConfigureAwait(false);
        var list = await workItemClient.GetWorkItemsAsync(workItemsIds.WorkItems.Select(x => x.Id).ToArray());
        
        foreach (var item in list)
        {
            Console.WriteLine($"#{item.Id} - [{item.Fields["System.WorkItemType"]}] - {item.Fields["System.Title"]} - {item.Fields["System.State"]}");
        }
        
        return list.ToList();
    }

    /// <summary>
    /// 檢查 Redmine Issue 是否已經被遷移到 Azure DevOps
    /// </summary>
    public async Task<WorkItem?> FindExistingWorkItemAsync(int redmineIssueId)
    {
        try
        {
            using var workItemClient = _connection.GetClient<WorkItemTrackingHttpClient>();

            var wiql = new Wiql
            {
                Query = $@"Select [System.Id] From WorkItems 
                          Where [System.Tags] Contains 'Redmine-{redmineIssueId}' 
                          OR [System.Description] Contains 'Redmine Issue #{redmineIssueId}'
                          OR [System.Title] Contains '[{redmineIssueId}]'"
            };

            var workItemsIds = await workItemClient.QueryByWiqlAsync(wiql, _config.ProjectName).ConfigureAwait(false);
            
            if (workItemsIds.WorkItems.Any())
            {
                var workItems = await workItemClient.GetWorkItemsAsync(workItemsIds.WorkItems.Select(x => x.Id).ToArray());
                return workItems.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"查詢現有 Work Item 時發生錯誤: {ex.Message}");
        }
        
        return null;
    }

    /// <summary>
    /// 建立新的 Work Item 從 Redmine Issue
    /// </summary>
    public async Task<WorkItem?> CreateWorkItemFromIssueAsync(Issue redmineIssue)
    {
        try
        {
            using var workItemClient = _connection.GetClient<WorkItemTrackingHttpClient>();

            var workItemType = GetMappedWorkItemType(redmineIssue.Tracker?.Name ?? "Task");
            var mappedState = GetMappedState(redmineIssue.Status?.Name ?? "New");

            var document = new JsonPatchDocument();
            
            // 基本欄位
            document.Add(new JsonPatchOperation()
            {
                Operation = Operation.Add,
                Path = "/fields/System.Title",
                Value = $"[{workItemType}][{redmineIssue.Id}] {redmineIssue.Subject}"
            });

            document.Add(new JsonPatchOperation()
            {
                Operation = Operation.Add,
                Path = "/fields/System.Description",
                Value = FormatWorkItemDescription(redmineIssue)
            });

            document.Add(new JsonPatchOperation()
            {
                Operation = Operation.Add,
                Path = "/fields/System.State",
                Value = mappedState
            });

            // 指派給誰 - 先跳過，避免使用者不存在的問題
            // TODO: 可以後續實作使用者對應表
            /*
            if (redmineIssue.AssignedTo != null)
            {
                document.Add(new JsonPatchOperation()
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.AssignedTo",
                    Value = redmineIssue.AssignedTo.Name
                });
            }
            */

            // 優先權
            if (redmineIssue.Priority != null)
            {
                var priority = MapPriority(redmineIssue.Priority.Name);
                document.Add(new JsonPatchOperation()
                {
                    Operation = Operation.Add,
                    Path = "/fields/Microsoft.VSTS.Common.Priority",
                    Value = priority
                });
            }

            // 標籤 - 用來標識這是從 Redmine 遷移的
            document.Add(new JsonPatchOperation()
            {
                Operation = Operation.Add,
                Path = "/fields/System.Tags",
                Value = $"Redmine-{redmineIssue.Id};Migrated"
            });

            // 建立日期
            if (redmineIssue.CreatedOn.HasValue)
            {
                document.Add(new JsonPatchOperation()
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.CreatedDate",
                    Value = redmineIssue.CreatedOn.Value
                });
            }

            var result = await workItemClient.CreateWorkItemAsync(document, _config.ProjectName, workItemType);
            Console.WriteLine($"成功建立 Work Item #{result.Id} 從 Redmine Issue #{redmineIssue.Id}");
            
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"建立 Work Item 失敗 (Redmine Issue #{redmineIssue.Id}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 更新現有 Work Item 的狀態
    /// </summary>
    public async Task<WorkItem?> UpdateWorkItemStatusAsync(int workItemId, string newStatus)
    {
        try
        {
            using var workItemClient = _connection.GetClient<WorkItemTrackingHttpClient>();

            var document = new JsonPatchDocument();
            document.Add(new JsonPatchOperation()
            {
                Operation = Operation.Replace,
                Path = "/fields/System.State",
                Value = newStatus
            });

            var result = await workItemClient.UpdateWorkItemAsync(document, workItemId);
            Console.WriteLine($"更新 Work Item #{workItemId} 狀態為: {newStatus}");
            
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"更新 Work Item #{workItemId} 狀態失敗: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 上傳附件到 Work Item
    /// </summary>
    public async Task<bool> UploadAttachmentAsync(int workItemId, byte[] fileContent, string fileName)
    {
        try
        {
            using var workItemClient = _connection.GetClient<WorkItemTrackingHttpClient>();

            // 首先上傳附件
            var attachmentRef = await workItemClient.CreateAttachmentAsync(
                uploadStream: new MemoryStream(fileContent), 
                fileName: fileName,
                uploadType: null,
                areaPath: null,
                userState: null);

            // 然後將附件關聯到 Work Item
            var document = new JsonPatchDocument();
            document.Add(new JsonPatchOperation()
            {
                Operation = Operation.Add,
                Path = "/relations/-",
                Value = new
                {
                    rel = "AttachedFile",
                    url = attachmentRef.Url,
                    attributes = new
                    {
                        comment = $"從 Redmine 遷移的附件: {fileName}"
                    }
                }
            });

            await workItemClient.UpdateWorkItemAsync(document, workItemId);
            Console.WriteLine($"成功上傳附件 {fileName} 到 Work Item #{workItemId}");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"上傳附件失敗 {fileName} 到 Work Item #{workItemId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 取得指定的 Work Item
    /// </summary>
    public async Task<WorkItem?> GetWorkItemAsync(int workItemId)
    {
        try
        {
            using var workItemClient = _connection.GetClient<WorkItemTrackingHttpClient>();
            var workItem = await workItemClient.GetWorkItemAsync(workItemId);
            return workItem;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"取得 Work Item #{workItemId} 失敗: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 更新 Work Item (支援批次更新多個欄位)
    /// </summary>
    public async Task<WorkItem?> UpdateWorkItemAsync(int workItemId, WorkItem sourceWorkItem)
    {
        try
        {
            using var workItemClient = _connection.GetClient<WorkItemTrackingHttpClient>();

            var document = new JsonPatchDocument();
            
            // 只更新允許修改的欄位，避免覆蓋系統欄位
            if (sourceWorkItem.Fields.ContainsKey("System.WorkItemType"))
            {
                document.Add(new JsonPatchOperation()
                {
                    Operation = Operation.Replace,
                    Path = "/fields/System.WorkItemType",
                    Value = sourceWorkItem.Fields["System.WorkItemType"]
                });
            }

            if (sourceWorkItem.Fields.ContainsKey("System.State"))
            {
                document.Add(new JsonPatchOperation()
                {
                    Operation = Operation.Replace,
                    Path = "/fields/System.State",
                    Value = sourceWorkItem.Fields["System.State"]
                });
            }

            if (sourceWorkItem.Fields.ContainsKey("System.Title"))
            {
                document.Add(new JsonPatchOperation()
                {
                    Operation = Operation.Replace,
                    Path = "/fields/System.Title",
                    Value = sourceWorkItem.Fields["System.Title"]
                });
            }

            var result = await workItemClient.UpdateWorkItemAsync(document, workItemId);
            Console.WriteLine($"更新 Work Item #{workItemId} 成功");
            
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"更新 Work Item #{workItemId} 失敗: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 修正 Work Item 類型
    /// </summary>
    public async Task<WorkItem?> CorrectWorkItemTypeAsync(int workItemId, string newWorkItemType)
    {
        try
        {
            using var workItemClient = _connection.GetClient<WorkItemTrackingHttpClient>();

            var document = new JsonPatchDocument();
            document.Add(new JsonPatchOperation()
            {
                Operation = Operation.Replace,
                Path = "/fields/System.WorkItemType",
                Value = newWorkItemType
            });

            var result = await workItemClient.UpdateWorkItemAsync(document, workItemId);
            Console.WriteLine($"修正 Work Item #{workItemId} 類型為: {newWorkItemType}");
            
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"修正 Work Item #{workItemId} 類型失敗: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 格式化工作項目描述為美觀的 HTML 格式
    /// </summary>
    private string FormatWorkItemDescription(Issue redmineIssue)
    {
        var html = new System.Text.StringBuilder();
        
        // 主要描述內容
        if (!string.IsNullOrWhiteSpace(redmineIssue.Description))
        {
            // 處理描述內容，改善格式
            var description = ProcessDescriptionContent(redmineIssue.Description);
            
            html.AppendLine("<div style='background-color: #ffffff; padding: 15px; margin-bottom: 20px; border-radius: 8px; border: 1px solid #e1e4e8;'>");
            html.AppendLine("  <h4 style='color: #0366d6; margin-top: 0; margin-bottom: 10px; font-size: 16px;'>📝 問題描述</h4>");
            html.AppendLine($"  <div style='line-height: 1.6; color: #24292e;'>{description}</div>");
            html.AppendLine("</div>");
        }
        
        // Redmine 來源資訊區塊
        html.AppendLine("<div style='background-color: #f6f8fa; border: 1px solid #d0d7de; border-radius: 8px; padding: 16px; margin: 16px 0;'>");
        html.AppendLine("  <h4 style='color: #0969da; margin-top: 0; margin-bottom: 12px; font-size: 14px; display: flex; align-items: center;'>");
        html.AppendLine("    <span style='margin-right: 8px;'>�</span>");
        html.AppendLine("    <span>原始 Redmine Issue 資訊</span>");
        html.AppendLine("  </h4>");
        
        // 使用卡片式佈局顯示資訊
        html.AppendLine("  <div style='display: grid; grid-template-columns: repeat(auto-fit, minmax(250px, 1fr)); gap: 12px;'>");
        
        // 基本資訊卡片
        html.AppendLine("    <div style='background: white; border: 1px solid #d0d7de; border-radius: 6px; padding: 12px;'>");
        html.AppendLine("      <div style='font-weight: 600; color: #0969da; margin-bottom: 8px; font-size: 13px;'>� 基本資訊</div>");
        html.AppendLine($"      <div style='margin-bottom: 4px;'><strong>Issue ID:</strong> <code>#{redmineIssue.Id}</code></div>");
        
        if (redmineIssue.Tracker != null)
        {
            var trackerColor = GetTrackerColor(redmineIssue.Tracker.Name);
            html.AppendLine($"      <div style='margin-bottom: 4px;'><strong>類型:</strong> <span style='background: {trackerColor}; color: white; padding: 2px 6px; border-radius: 3px; font-size: 11px;'>{redmineIssue.Tracker.Name}</span></div>");
        }
        
        if (redmineIssue.Status != null)
        {
            var statusColor = GetStatusColor(redmineIssue.Status.Name);
            html.AppendLine($"      <div><strong>狀態:</strong> <span style='background: {statusColor}; color: white; padding: 2px 6px; border-radius: 3px; font-size: 11px;'>{redmineIssue.Status.Name}</span></div>");
        }
        html.AppendLine("    </div>");
        
        // 人員資訊卡片
        html.AppendLine("    <div style='background: white; border: 1px solid #d0d7de; border-radius: 6px; padding: 12px;'>");
        html.AppendLine("      <div style='font-weight: 600; color: #0969da; margin-bottom: 8px; font-size: 13px;'>👥 人員資訊</div>");
        
        if (redmineIssue.Author != null)
        {
            html.AppendLine($"      <div style='margin-bottom: 4px;'><strong>建立者:</strong> {redmineIssue.Author.Name}</div>");
        }
        
        html.AppendLine($"      <div><strong>指派給:</strong> {redmineIssue.AssignedTo?.Name ?? "<span style='color: #6a737d;'>未指派</span>"}</div>");
        html.AppendLine("    </div>");
        
        // 時間與優先權資訊卡片
        html.AppendLine("    <div style='background: white; border: 1px solid #d0d7de; border-radius: 6px; padding: 12px;'>");
        html.AppendLine("      <div style='font-weight: 600; color: #0969da; margin-bottom: 8px; font-size: 13px;'>⏰ 時間資訊</div>");
        
        if (redmineIssue.CreatedOn.HasValue)
        {
            html.AppendLine($"      <div style='margin-bottom: 4px;'><strong>建立:</strong> {redmineIssue.CreatedOn.Value:yyyy-MM-dd HH:mm}</div>");
        }
        
        if (redmineIssue.UpdatedOn.HasValue)
        {
            html.AppendLine($"      <div style='margin-bottom: 4px;'><strong>更新:</strong> {redmineIssue.UpdatedOn.Value:yyyy-MM-dd HH:mm}</div>");
        }
        
        if (redmineIssue.Priority != null)
        {
            var priorityColor = GetPriorityColor(redmineIssue.Priority.Name);
            html.AppendLine($"      <div><strong>優先權:</strong> <span style='background: {priorityColor}; color: white; padding: 2px 6px; border-radius: 3px; font-size: 11px;'>{redmineIssue.Priority.Name}</span></div>");
        }
        html.AppendLine("    </div>");
        
        html.AppendLine("  </div>");
        html.AppendLine("</div>");
        
        // 遷移標記
        html.AppendLine("<div style='text-align: center; margin-top: 16px; padding: 12px; background: linear-gradient(90deg, #e8f5e8, #d4edda); border-radius: 6px; border: 1px solid #c3e6cb;'>");
        html.AppendLine("  <span style='color: #155724; font-weight: 600; font-size: 12px;'>🚀 此工作項目由 Redmine 自動遷移產生</span>");
        html.AppendLine("</div>");
        
        return html.ToString();
    }

    /// <summary>
    /// 處理描述內容，改善格式化
    /// </summary>
    private string ProcessDescriptionContent(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "";

        var processed = description;

        // 處理 Redmine 圖片語法 ![](filename) -> 顯示為附件引用
        processed = System.Text.RegularExpressions.Regex.Replace(
            processed,
            @"!\[\]\(([^)]+)\)",
            match =>
            {
                var filename = match.Groups[1].Value;
                return $"<div style='background: #fff3cd; border: 1px solid #ffeaa7; border-radius: 4px; padding: 8px; margin: 8px 0; display: inline-block;'>" +
                       $"<span style='color: #856404;'>� 圖片附件: <code>{filename}</code></span>" +
                       $"</div>";
            });

        // 處理連結
        processed = System.Text.RegularExpressions.Regex.Replace(
            processed,
            @"https?://[^\s]+",
            match => $"<a href='{match.Value}' target='_blank' style='color: #0366d6; text-decoration: none;'>{match.Value}</a>");

        // 處理換行
        processed = processed
            .Replace("\r\n", "<br/>")
            .Replace("\n", "<br/>")
            .Replace("\r", "<br/>");

        // 處理多個連續的 <br/> 標籤
        processed = System.Text.RegularExpressions.Regex.Replace(processed, @"(<br\s*/?>){3,}", "<br/><br/>");

        return processed;
    }

    /// <summary>
    /// 根據追蹤器類型獲取顏色
    /// </summary>
    private string GetTrackerColor(string trackerName)
    {
        return trackerName?.ToLower() switch
        {
            var t when t.Contains("錯誤") || t.Contains("臭蟲") || t.Contains("bug") => "#d73a49",
            var t when t.Contains("功能") || t.Contains("需求") || t.Contains("feature") => "#28a745",
            var t when t.Contains("任務") || t.Contains("調整") || t.Contains("task") => "#0366d6",
            _ => "#6f42c1"
        };
    }

    /// <summary>
    /// 根據狀態獲取顏色
    /// </summary>
    private string GetStatusColor(string statusName)
    {
        return statusName?.ToLower() switch
        {
            var s when s.Contains("新") || s.Contains("new") => "#0366d6",
            var s when s.Contains("進行") || s.Contains("實作") || s.Contains("active") => "#fd7e14",
            var s when s.Contains("測試") || s.Contains("resolved") => "#6f42c1",
            var s when s.Contains("結束") || s.Contains("關閉") || s.Contains("closed") => "#28a745",
            _ => "#6a737d"
        };
    }

    /// <summary>
    /// 根據優先權獲取顏色
    /// </summary>
    private string GetPriorityColor(string priorityName)
    {
        return priorityName?.ToLower() switch
        {
            var p when p.Contains("低") || p.Contains("low") => "#28a745",
            var p when p.Contains("一般") || p.Contains("normal") => "#0366d6",
            var p when p.Contains("高") || p.Contains("high") => "#fd7e14",
            var p when p.Contains("緊急") || p.Contains("urgent") || p.Contains("立即") => "#d73a49",
            _ => "#6a737d"
        };
    }

    private string GetMappedWorkItemType(string redmineTracker)
    {
        return _migrationConfig.WorkItemTypeMapping.GetValueOrDefault(redmineTracker, "Task");
    }

    private string GetMappedState(string redmineStatus)
    {
        return _migrationConfig.StatusMapping.GetValueOrDefault(redmineStatus, "New");
    }

    private int MapPriority(string redminePriority)
    {
        return redminePriority?.ToLower() switch
        {
            "低" or "low" => 4,
            "一般" or "normal" => 3,
            "高" or "high" => 2,
            "緊急" or "urgent" => 1,
            "立即" or "immediate" => 1,
            _ => 3
        };
    }
}