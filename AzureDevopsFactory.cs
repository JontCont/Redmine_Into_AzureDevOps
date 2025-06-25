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
                Value = $"{redmineIssue.Description}\n\n" +
                        $"[原始 Redmine Issue #{redmineIssue.Id}]\n" +
                        $"建立者: {redmineIssue.Author?.Name}\n" +
                        $"指派給: {redmineIssue.AssignedTo?.Name ?? "未指派"}"
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