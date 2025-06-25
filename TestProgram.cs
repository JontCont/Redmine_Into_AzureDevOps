using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using redmineApi.Models;
using redmineApi.Services;

namespace redmineApi
{
    static class TestProgram
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== Redmine 到 Azure DevOps 單筆測試工具 ===\n");

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
                var migrationService = serviceProvider.GetRequiredService<IMigrationService>();
                var redmineFactory = serviceProvider.GetRequiredService<RedmineFactory>();
                var azureFactory = serviceProvider.GetRequiredService<AzureDevopsFactory>();

                logger.LogInformation("開始測試單筆遷移...");

                // 步驟 1: 取得 Redmine Issues 列表
                Console.WriteLine("=== 步驟 1: 取得 Redmine Issues ===");
                var issues = redmineFactory.GetIssues("5"); // 只取 5 筆測試
                
                if (!issues.Any())
                {
                    Console.WriteLine("❌ 沒有找到任何 Redmine Issues");
                    return;
                }

                Console.WriteLine($"✅ 找到 {issues.Count} 個 Issues:");
                for (int i = 0; i < issues.Count; i++)
                {
                    var issue = issues[i];
                    Console.WriteLine($"  [{i + 1}] #{issue.Id} - {issue.Subject}");
                    Console.WriteLine($"      狀態: {issue.Status?.Name} | 類型: {issue.Tracker?.Name}");
                    Console.WriteLine($"      附件: {issue.Attachments?.Count() ?? 0} 個");
                }

                // 步驟 2: 選擇要測試的 Issue
                Console.Write($"\n請選擇要測試的 Issue (1-{issues.Count}): ");
                var choice = Console.ReadLine();
                
                if (!int.TryParse(choice, out int selectedIndex) || selectedIndex < 1 || selectedIndex > issues.Count)
                {
                    Console.WriteLine("無效的選擇");
                    return;
                }

                var selectedIssue = issues[selectedIndex - 1];
                Console.WriteLine($"\n=== 步驟 2: 測試遷移 Issue #{selectedIssue.Id} ===");

                // 步驟 3: 檢查 Azure DevOps 連線
                Console.WriteLine("=== 步驟 3: 檢查 Azure DevOps 連線 ===");
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
                var existingWorkItem = await azureFactory.FindExistingWorkItemAsync(selectedIssue.Id);
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

                // 步驟 5: 建立 Work Item
                Console.WriteLine("\n=== 步驟 5: 建立 Work Item ===");
                var newWorkItem = await azureFactory.CreateWorkItemFromIssueAsync(selectedIssue);
                
                if (newWorkItem?.Id != null)
                {
                    Console.WriteLine($"✅ 成功建立 Work Item #{newWorkItem.Id}");
                    Console.WriteLine($"   標題: {newWorkItem.Fields["System.Title"]}");
                    Console.WriteLine($"   狀態: {newWorkItem.Fields["System.State"]}");
                    Console.WriteLine($"   類型: {newWorkItem.Fields["System.WorkItemType"]}");

                    // 步驟 6: 測試附件上傳（如果有）
                    if (selectedIssue.Attachments?.Any() == true)
                    {
                        Console.WriteLine($"\n=== 步驟 6: 測試附件上傳 ({selectedIssue.Attachments.Count()} 個附件) ===");
                        
                        var firstAttachment = selectedIssue.Attachments.First();
                        Console.WriteLine($"測試上傳附件: {firstAttachment.FileName}");
                        
                        try
                        {
                            var fileContent = await redmineFactory.DownloadAttachmentAsync(firstAttachment);
                            if (fileContent != null)
                            {
                                Console.WriteLine($"✅ 成功下載附件 ({fileContent.Length} bytes)");
                                
                                var uploadSuccess = await azureFactory.UploadAttachmentAsync(
                                    newWorkItem.Id.Value, 
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
                        Console.WriteLine("\n=== 步驟 6: 跳過附件測試 (沒有附件) ===");
                    }

                    // 步驟 7: 測試狀態更新
                    Console.WriteLine("\n=== 步驟 7: 測試狀態更新 ===");
                    var currentStatus = newWorkItem.Fields["System.State"].ToString();
                    Console.WriteLine($"目前狀態: {currentStatus}");
                    
                    // 嘗試更新為不同狀態
                    var testStatus = currentStatus == "New" ? "Active" : "New";
                    Console.WriteLine($"嘗試更新狀態為: {testStatus}");
                    
                    var updatedWorkItem = await azureFactory.UpdateWorkItemStatusAsync(newWorkItem.Id.Value, testStatus);
                    if (updatedWorkItem != null)
                    {
                        Console.WriteLine($"✅ 狀態更新成功: {updatedWorkItem.Fields["System.State"]}");
                    }
                    else
                    {
                        Console.WriteLine("❌ 狀態更新失敗");
                    }

                    Console.WriteLine($"\n🎉 測試完成！Work Item 已建立: https://dev.azure.com/{appConfig.AzureDevOps.OrganizationUrl.Split('/').Last()}/{appConfig.AzureDevOps.ProjectName}/_workitems/edit/{newWorkItem.Id}");
                }
                else
                {
                    Console.WriteLine("❌ Work Item 建立失敗");
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
