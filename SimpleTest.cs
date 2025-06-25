using Microsoft.Extensions.Configuration;
using redmineApi.Models;

namespace redmineApi
{
    static class SimpleTest
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== 簡單連線測試 ===\n");

            try
            {
                // 設定配置
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();

                var appConfig = new AppConfig();
                configuration.Bind(appConfig);

                // 測試 Redmine 連線
                Console.WriteLine("=== 測試 Redmine 連線 ===");
                var redmineFactory = new RedmineFactory(appConfig.Redmine);
                
                try
                {
                    var issues = redmineFactory.GetIssues("3");
                    Console.WriteLine($"✅ Redmine 連線成功！找到 {issues.Count} 個 Issues:");
                    
                    foreach (var issue in issues)
                    {
                        Console.WriteLine($"  #{issue.Id} - {issue.Subject}");
                        Console.WriteLine($"    狀態: {issue.Status?.Name}");
                        Console.WriteLine($"    類型: {issue.Tracker?.Name}");
                        Console.WriteLine($"    附件: {issue.Attachments?.Count() ?? 0} 個");
                        Console.WriteLine();
                    }

                    // 測試取得詳細資訊
                    if (issues.Any())
                    {
                        var firstIssue = issues.First();
                        Console.WriteLine($"=== 測試取得 Issue #{firstIssue.Id} 詳細資訊 ===");
                        var detailedIssue = redmineFactory.GetIssueWithDetails(firstIssue.Id);
                        
                        if (detailedIssue != null)
                        {
                            Console.WriteLine($"✅ 成功取得詳細資訊");
                            Console.WriteLine($"描述長度: {detailedIssue.Description?.Length ?? 0} 字元");
                            Console.WriteLine($"建立者: {detailedIssue.Author?.Name}");
                            Console.WriteLine($"指派給: {detailedIssue.AssignedTo?.Name ?? "未指派"}");
                            
                            if (detailedIssue.Attachments?.Any() == true)
                            {
                                Console.WriteLine("附件列表:");
                                foreach (var attachment in detailedIssue.Attachments)
                                {
                                    Console.WriteLine($"  - {attachment.FileName} ({attachment.FileSize} bytes)");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("❌ 無法取得詳細資訊");
                        }
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Redmine 連線失敗: {ex.Message}");
                    Console.WriteLine($"詳細錯誤: {ex}");
                }

                // 測試 Azure DevOps 連線
                Console.WriteLine("\n=== 測試 Azure DevOps 連線 ===");
                try
                {
                    var azureFactory = new AzureDevopsFactory(appConfig.AzureDevOps, appConfig.Migration);
                    var project = await azureFactory.GetProject();
                    Console.WriteLine($"✅ Azure DevOps 連線成功！專案: {project.Name}");
                    
                    // 測試查詢 Work Items
                    var workItems = await azureFactory.GetWorkItems();
                    Console.WriteLine($"✅ 成功查詢到 {workItems.Count} 個 Work Items");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Azure DevOps 連線失敗: {ex.Message}");
                    Console.WriteLine("請檢查:");
                    Console.WriteLine("1. Personal Access Token 是否正確且未過期");
                    Console.WriteLine("2. 組織 URL 是否正確");
                    Console.WriteLine("3. 專案名稱是否正確");
                    Console.WriteLine("4. Token 是否有足夠的權限 (Work Items: Read & Write)");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"程式執行錯誤: {ex.Message}");
            }

            Console.WriteLine("\n按任意鍵結束程式...");
            Console.ReadKey();
        }
    }
}
