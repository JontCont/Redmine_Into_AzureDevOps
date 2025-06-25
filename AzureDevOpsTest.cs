using Microsoft.Extensions.Configuration;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using redmineApi.Models;

namespace redmineApi
{
    static class AzureDevOpsTest
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== Azure DevOps 詳細連線測試 ===\n");

            try
            {
                // 設定配置
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();

                var appConfig = new AppConfig();
                configuration.Bind(appConfig);

                Console.WriteLine($"組織 URL: {appConfig.AzureDevOps.OrganizationUrl}");
                Console.WriteLine($"專案名稱: {appConfig.AzureDevOps.ProjectName}");
                Console.WriteLine($"Token: {appConfig.AzureDevOps.PersonalAccessToken[..10]}...(已隱藏)\n");

                // 測試基本連線
                Console.WriteLine("=== 步驟 1: 測試基本連線 ===");
                try
                {
                    var connection = new VssConnection(
                        new Uri(appConfig.AzureDevOps.OrganizationUrl), 
                        new VssBasicCredential(string.Empty, appConfig.AzureDevOps.PersonalAccessToken));

                    // 測試連線
                    await connection.ConnectAsync();
                    Console.WriteLine("✅ 基本連線成功");

                    // 測試取得使用者資訊
                    var userInfo = await connection.GetClientAsync<ProjectHttpClient>();
                    Console.WriteLine("✅ 成功取得客戶端");

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 基本連線失敗: {ex.Message}");
                    Console.WriteLine("\n可能的解決方案:");
                    Console.WriteLine("1. 檢查組織 URL 是否正確 (例如: https://dev.azure.com/你的組織名稱/)");
                    Console.WriteLine("2. 確認 Personal Access Token 未過期");
                    Console.WriteLine("3. 確認 Token 有正確的權限範圍");
                    return;
                }

                // 測試列出專案
                Console.WriteLine("\n=== 步驟 2: 列出可用專案 ===");
                try
                {
                    var connection = new VssConnection(
                        new Uri(appConfig.AzureDevOps.OrganizationUrl), 
                        new VssBasicCredential(string.Empty, appConfig.AzureDevOps.PersonalAccessToken));

                    var projectClient = connection.GetClient<ProjectHttpClient>();
                    var projects = await projectClient.GetProjects();

                    Console.WriteLine($"✅ 找到 {projects.Count} 個專案:");
                    foreach (var project in projects)
                    {
                        Console.WriteLine($"  - {project.Name} (ID: {project.Id})");
                        if (project.Name == appConfig.AzureDevOps.ProjectName)
                        {
                            Console.WriteLine("    ✅ 這是設定檔中指定的專案");
                        }
                    }

                    // 檢查指定專案是否存在
                    var targetProject = projects.FirstOrDefault(p => p.Name == appConfig.AzureDevOps.ProjectName);
                    if (targetProject == null)
                    {
                        Console.WriteLine($"\n❌ 找不到專案 '{appConfig.AzureDevOps.ProjectName}'");
                        Console.WriteLine("請確認專案名稱是否正確，或選擇上面列出的其中一個專案");
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"\n✅ 確認專案 '{appConfig.AzureDevOps.ProjectName}' 存在");
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 列出專案失敗: {ex.Message}");
                    return;
                }

                // 測試 Work Items 權限
                Console.WriteLine("\n=== 步驟 3: 測試 Work Items 權限 ===");
                try
                {
                    var azureFactory = new AzureDevopsFactory(appConfig.AzureDevOps, appConfig.Migration);
                    var workItems = await azureFactory.GetWorkItems();
                    Console.WriteLine($"✅ Work Items 權限正常，找到 {workItems.Count} 個項目");

                    if (workItems.Any())
                    {
                        var firstItem = workItems.First();
                        Console.WriteLine($"第一個 Work Item: #{firstItem.Id} - {firstItem.Fields.GetValueOrDefault("System.Title", "無標題")}");
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Work Items 權限測試失敗: {ex.Message}");
                    Console.WriteLine("\n請確認 Personal Access Token 包含以下權限:");
                    Console.WriteLine("- Work Items: Read & Write");
                    Console.WriteLine("- Project and Team: Read");
                    return;
                }

                Console.WriteLine("\n🎉 所有測試通過！Azure DevOps 連線正常");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"程式執行錯誤: {ex.Message}");
                Console.WriteLine($"詳細錯誤: {ex}");
            }

            Console.WriteLine("\n按任意鍵結束程式...");
            Console.ReadKey();
        }
    }
}
