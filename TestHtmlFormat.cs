using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using redmineApi.Models;
using redmineApi.Services;

namespace redmineApi;

/// <summary>
/// 測試新的 HTML 格式化功能
/// </summary>
public class TestHtmlFormat
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== 測試新的 HTML 格式化功能 ===\n");

        var host = CreateHostBuilder(args).Build();
        var redmineFactory = host.Services.GetRequiredService<RedmineFactory>();

        // 測試獲取 Issue #73596 並顯示新的格式
        await TestIssueFormatting(redmineFactory);

        Console.WriteLine("\n測試完成，按任意鍵結束...");
        Console.ReadKey();
    }

    private static async Task TestIssueFormatting(RedmineFactory redmineFactory)
    {
        try
        {
            Console.WriteLine("正在獲取 Issue #73596...");
            
            var issue = redmineFactory.GetIssueWithDetails(73596);
            if (issue == null)
            {
                Console.WriteLine("❌ 無法獲取 Issue #73596");
                return;
            }

            Console.WriteLine($"✅ 成功獲取 Issue: {issue.Subject}");
            Console.WriteLine();

            // 創建 AzureDevopsFactory 實例來測試格式化
            var appConfig = new AppConfig
            {
                AzureDevOps = new AzureDevOpsConfig
                {
                    OrganizationUrl = "https://dev.azure.com/test/",
                    PersonalAccessToken = "test",
                    ProjectName = "test"
                },
                Migration = new MigrationConfig()
            };

            var azureFactory = new AzureDevopsFactory(appConfig.AzureDevOps, appConfig.Migration);
            
            // 使用反射呼叫私有方法來測試格式化
            var method = typeof(AzureDevopsFactory).GetMethod("FormatWorkItemDescription", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (method != null)
            {
                var formattedHtml = method.Invoke(azureFactory, new object[] { issue }) as string;
                
                Console.WriteLine("=== 新的 HTML 格式預覽 ===");
                Console.WriteLine("(此 HTML 在 Azure DevOps 中會顯示為美觀的格式)");
                Console.WriteLine();
                Console.WriteLine(formattedHtml);
                Console.WriteLine();
                
                // 將 HTML 儲存到檔案以便在瀏覽器中預覽
                var htmlFile = Path.Combine(Directory.GetCurrentDirectory(), "issue_73596_preview.html");
                var fullHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Issue #73596 格式預覽</title>
    <style>
        body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 20px; }}
        .container {{ max-width: 800px; margin: 0 auto; }}
    </style>
</head>
<body>
    <div class='container'>
        <h1>Issue #73596 新格式預覽</h1>
        {formattedHtml}
    </div>
</body>
</html>";
                
                await File.WriteAllTextAsync(htmlFile, fullHtml);
                Console.WriteLine($"✅ HTML 預覽檔案已儲存至: {htmlFile}");
                Console.WriteLine("您可以用瀏覽器開啟此檔案來查看實際效果");
            }
            else
            {
                Console.WriteLine("❌ 無法存取格式化方法");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"測試過程中發生錯誤: {ex.Message}");
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;
                
                var appConfig = configuration.Get<AppConfig>() ?? new AppConfig();
                services.AddSingleton(appConfig);
                services.AddSingleton(appConfig.Redmine);
                services.AddSingleton(appConfig.AzureDevOps);
                services.AddSingleton(appConfig.Migration);

                services.AddScoped<RedmineFactory>();
                services.AddScoped<AzureDevopsFactory>();
                services.AddScoped<IMigrationTrackingService, FileMigrationTrackingService>();
                services.AddScoped<IMigrationService, MigrationService>();
            });
}
