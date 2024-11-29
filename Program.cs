using Newtonsoft.Json;

namespace redmineApi
{
    static class Program
    {
        public static async Task Main(string[] args)
        {
            // Redmine 設定
            var redmineFactory = new RedmineFactory("PROJECT_NAME");
            var list = redmineFactory.GetIssues();

            //AzureDevops 設定
            var azureDevopsFactory = new AzureDevopsFactory("PROJECT_NAME");
            await azureDevopsFactory.GetWorkItems().ConfigureAwait(false);
        }
    }
}