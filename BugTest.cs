using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using redmineApi.Models;
using redmineApi.Services;

namespace redmineApi
{
    static class BugTest
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== Redmine Bug 遷移到 Azure DevOps 測試 ===\n");

            try
            {
                // 設定配置
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();

                var appConfig = new AppConfig();
                configuration.Bind(appConfig);

                // 設定服務
                var services = new ServiceCollection();
                ConfigureServices(services, appConfig);
                var serviceProvider = services.BuildServiceProvider();

                // 取得服務
                var logger = serviceProvider.GetRequiredService<ILogger<MigrationService>>();
                var redmineFactory = serviceProvider.GetRequiredService<RedmineFactory>();
                var azureFactory = serviceProvider.GetRequiredService<AzureDevopsFactory>();

                logger.LogInformation("開始測試 Bug 遷移...");

                // 步驟 1: 尋找 Bug 類型的 Issues
                Console.WriteLine("=== 步驟 1: 尋找 Bug 類型的 Redmine Issues ===");
                var allIssues = redmineFactory.GetIssues("50"); // 取得更多 issues 來找 bug
                
                var bugIssues = allIssues.Where(issue => 
                    issue.Tracker?.Name?.Contains("臭蟲") == true ||
                    issue.Tracker?.Name?.Contains("Bug") == true ||
                    issue.Tracker?.Name?.Contains("錯誤") == true ||
                    issue.Subject?.Contains("錯誤") == true ||
                    issue.Subject?.Contains("Bug") == true ||
                    issue.Subject?.Contains("問題") == true).ToList();

                if (!bugIssues.Any())
                {
                    Console.WriteLine("❌ 沒有找到 Bug 類型的 Issues");
                    Console.WriteLine("\n所有可用的 Issues:");
                    for (int i = 0; i < Math.Min(allIssues.Count, 10); i++)
                    {
                        var issue = allIssues[i];
                        Console.WriteLine($"  [{i + 1}] #{issue.Id} - {issue.Subject}");
                        Console.WriteLine($"      類型: {issue.Tracker?.Name} | 狀態: {issue.Status?.Name}");
                    }
                    
                    Console.Write($"\n請選擇要當作 Bug 測試的 Issue (1-{Math.Min(allIssues.Count, 10)}): ");
                    var choice = Console.ReadLine();
                    
                    if (int.TryParse(choice, out int selectedIndex) && selectedIndex >= 1 && selectedIndex <= Math.Min(allIssues.Count, 10))
                    {
                        bugIssues.Add(allIssues[selectedIndex - 1]);
                    }
                    else
                    {
                        Console.WriteLine("無效的選擇");
                        return;
                    }
                }

                Console.WriteLine($"✅ 找到 {bugIssues.Count} 個 Bug Issues:");
                for (int i = 0; i < bugIssues.Count; i++)
                {
                    var issue = bugIssues[i];
                    Console.WriteLine($"  [{i + 1}] #{issue.Id} - {issue.Subject}");
                    Console.WriteLine($"      類型: {issue.Tracker?.Name} | 狀態: {issue.Status?.Name}");
                    Console.WriteLine($"      附件: {issue.Attachments?.Count() ?? 0} 個");
                }

                // 步驟 2: 選擇要測試的 Bug
                Console.Write($"\n請選擇要測試的 Bug Issue (1-{bugIssues.Count}): ");
                var bugChoice = Console.ReadLine();
                
                if (!int.TryParse(bugChoice, out int selectedBugIndex) || selectedBugIndex < 1 || selectedBugIndex > bugIssues.Count)
                {
                    Console.WriteLine("無效的選擇");
                    return;
                }

                var selectedBug = bugIssues[selectedBugIndex - 1];
                Console.WriteLine($"\n=== 步驟 2: 測試遷移 Bug Issue #{selectedBug.Id} ===");
                Console.WriteLine($"標題: {selectedBug.Subject}");
                Console.WriteLine($"類型: {selectedBug.Tracker?.Name}");
                Console.WriteLine($"狀態: {selectedBug.Status?.Name}");
                Console.WriteLine($"描述: {selectedBug.Description?[..Math.Min(selectedBug.Description.Length, 100)]}...");

                // 步驟 3: 檢查 Azure DevOps 連線
                Console.WriteLine("\n=== 步驟 3: 檢查 Azure DevOps 連線 ===");
                try
                {
                    var project = await azureFactory.GetProject();
                    Console.WriteLine($"✅ 成功連接到 Azure DevOps 專案: {project.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Azure DevOps 連線失敗: {ex.Message}");
                    return;
                }

                // 步驟 4: 檢查是否已存在
                Console.WriteLine("\n=== 步驟 4: 檢查是否已存在對應的 Work Item ===");
                var existingWorkItem = await azureFactory.FindExistingWorkItemAsync(selectedBug.Id);
                if (existingWorkItem != null)
                {
                    Console.WriteLine($"⚠️  找到現有的 Work Item #{existingWorkItem.Id}");
                    Console.Write("是否要重新建立? (y/N): ");
                    var confirm = Console.ReadLine();
                    if (confirm?.ToLower() != "y")
                    {
                        Console.WriteLine("測試已取消");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine("✅ 沒有找到現有的 Work Item，可以建立新的");
                }

                // 步驟 5: 強制建立 Bug Work Item
                Console.WriteLine("\n=== 步驟 5: 建立 Bug Work Item ===");
                var bugWorkItem = await CreateBugWorkItemAsync(azureFactory, selectedBug, appConfig);
                
                if (bugWorkItem?.Id != null)
                {
                    Console.WriteLine($"✅ 成功建立 Bug Work Item #{bugWorkItem.Id}");
                    Console.WriteLine($"   標題: {bugWorkItem.Fields["System.Title"]}");
                    Console.WriteLine($"   狀態: {bugWorkItem.Fields["System.State"]}");
                    Console.WriteLine($"   類型: {bugWorkItem.Fields["System.WorkItemType"]}");

                    // 步驟 6: 測試 Bug 特定的欄位更新
                    Console.WriteLine("\n=== 步驟 6: 測試 Bug 特定欄位 ===");
                    await TestBugSpecificFields(azureFactory, bugWorkItem.Id.Value);

                    // 步驟 7: 測試附件（如果有）
                    if (selectedBug.Attachments?.Any() == true)
                    {
                        Console.WriteLine($"\n=== 步驟 7: 測試附件上傳 ({selectedBug.Attachments.Count()} 個附件) ===");
                        
                        var firstAttachment = selectedBug.Attachments.First();
                        Console.WriteLine($"測試上傳附件: {firstAttachment.FileName}");
                        
                        try
                        {
                            var fileContent = await redmineFactory.DownloadAttachmentAsync(firstAttachment);
                            if (fileContent != null)
                            {
                                Console.WriteLine($"✅ 成功下載附件 ({fileContent.Length} bytes)");
                                
                                var uploadSuccess = await azureFactory.UploadAttachmentAsync(
                                    bugWorkItem.Id.Value, 
                                    fileContent, 
                                    firstAttachment.FileName);
                                
                                if (uploadSuccess)
                                {
                                    Console.WriteLine("✅ 附件上傳成功");
                                }
                                else
                                {
                                    Console.WriteLine("❌ 附件上傳失敗");
                                }
                            }
                            else
                            {
                                Console.WriteLine("❌ 附件下載失敗");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ 附件處理錯誤: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("\n=== 步驟 7: 跳過附件測試 (沒有附件) ===");
                    }

                    var orgName = appConfig.AzureDevOps.OrganizationUrl.Split('/').Last(s => !string.IsNullOrEmpty(s));
                    Console.WriteLine($"\n🐛 Bug Work Item 建立完成！");
                    Console.WriteLine($"🔗 連結: https://dev.azure.com/{orgName}/{appConfig.AzureDevOps.ProjectName}/_workitems/edit/{bugWorkItem.Id}");
                }
                else
                {
                    Console.WriteLine("❌ Bug Work Item 建立失敗");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"程式執行錯誤: {ex.Message}");
                Console.WriteLine($"詳細錯誤: {ex}");
            }

            Console.WriteLine("\n按任意鍵結束程式...");
            Console.ReadKey();
        }

        private static async Task<Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem?> CreateBugWorkItemAsync(
            AzureDevopsFactory azureFactory, 
            Redmine.Net.Api.Types.Issue redmineBug,
            AppConfig config)
        {
            try
            {
                Console.WriteLine("強制建立為 Bug 類型...");
                
                // 直接呼叫 Azure DevOps API 建立 Bug
                using var workItemClient = azureFactory.GetClient<Microsoft.TeamFoundation.WorkItemTracking.WebApi.WorkItemTrackingHttpClient>();

                var document = new Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchDocument();
                
                // 基本欄位
                document.Add(new Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchOperation()
                {
                    Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                    Path = "/fields/System.Title",
                    Value = $"[Bug][{redmineBug.Id}] {redmineBug.Subject}"
                });

                document.Add(new Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchOperation()
                {
                    Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                    Path = "/fields/System.Description",
                    Value = $"<h3>Bug 描述</h3>\n{redmineBug.Description}\n\n" +
                            $"<h3>來源資訊</h3>\n" +
                            $"<ul>\n" +
                            $"<li>原始 Redmine Issue: #{redmineBug.Id}</li>\n" +
                            $"<li>建立者: {redmineBug.Author?.Name}</li>\n" +
                            $"<li>指派給: {redmineBug.AssignedTo?.Name ?? "未指派"}</li>\n" +
                            $"<li>原始類型: {redmineBug.Tracker?.Name}</li>\n" +
                            $"<li>建立日期: {redmineBug.CreatedOn:yyyy-MM-dd HH:mm:ss}</li>\n" +
                            $"</ul>"
                });

                // 設定為 New 狀態
                document.Add(new Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchOperation()
                {
                    Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                    Path = "/fields/System.State",
                    Value = "New"
                });

                // 優先權
                if (redmineBug.Priority != null)
                {
                    var priority = MapBugPriority(redmineBug.Priority.Name);
                    document.Add(new Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchOperation()
                    {
                        Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                        Path = "/fields/Microsoft.VSTS.Common.Priority",
                        Value = priority
                    });
                }

                // 嚴重性 (Bug 專用欄位)
                document.Add(new Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchOperation()
                {
                    Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                    Path = "/fields/Microsoft.VSTS.Common.Severity",
                    Value = "3 - Medium" // 預設中等嚴重性
                });

                // 標籤
                document.Add(new Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchOperation()
                {
                    Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                    Path = "/fields/System.Tags",
                    Value = $"Redmine-{redmineBug.Id};Migrated;Bug;FromRedmine"
                });

                var result = await workItemClient.CreateWorkItemAsync(document, config.AzureDevOps.ProjectName, "Bug");
                Console.WriteLine($"✅ 成功建立 Bug Work Item #{result.Id}");
                
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 建立 Bug Work Item 失敗: {ex.Message}");
                return null;
            }
        }

        private static async Task TestBugSpecificFields(AzureDevopsFactory azureFactory, int workItemId)
        {
            try
            {
                using var workItemClient = azureFactory.GetClient<Microsoft.TeamFoundation.WorkItemTracking.WebApi.WorkItemTrackingHttpClient>();

                var document = new Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchDocument();

                // 更新嚴重性
                document.Add(new Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchOperation()
                {
                    Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Replace,
                    Path = "/fields/Microsoft.VSTS.Common.Severity",
                    Value = "2 - High"
                });

                // 更新狀態到 Active
                document.Add(new Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchOperation()
                {
                    Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Replace,
                    Path = "/fields/System.State",
                    Value = "Active"
                });

                var result = await workItemClient.UpdateWorkItemAsync(document, workItemId);
                Console.WriteLine($"✅ 更新 Bug 特定欄位成功");
                
                var severityValue = result.Fields.TryGetValue("Microsoft.VSTS.Common.Severity", out var severity) ? severity?.ToString() : "未設定";
                var stateValue = result.Fields.TryGetValue("System.State", out var state) ? state?.ToString() : "未設定";
                
                Console.WriteLine($"   嚴重性: {severityValue}");
                Console.WriteLine($"   狀態: {stateValue}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 更新 Bug 特定欄位失敗: {ex.Message}");
            }
        }

        private static int MapBugPriority(string redminePriority)
        {
            return redminePriority?.ToLower() switch
            {
                "低" or "low" => 4,
                "一般" or "normal" => 3,
                "高" or "high" => 2,
                "緊急" or "urgent" => 1,
                "立即" or "immediate" => 1,
                _ => 2 // Bug 預設為高優先權
            };
        }

        private static void ConfigureServices(IServiceCollection services, AppConfig config)
        {
            // 註冊配置
            services.AddSingleton(config);
            services.AddSingleton(config.Redmine);
            services.AddSingleton(config.AzureDevOps);
            services.AddSingleton(config.Migration);

            // 註冊日誌
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // 註冊服務
            services.AddSingleton<RedmineFactory>();
            services.AddSingleton<AzureDevopsFactory>();
            services.AddSingleton<IMigrationTrackingService, FileMigrationTrackingService>();
            services.AddSingleton<IMigrationService, MigrationService>();
        }
    }
}
