using System;
using System.Collections.Specialized;
using Redmine.Net.Api;
using Redmine.Net.Api.Types;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using Microsoft.VisualStudio.Services.Commerce;
using System.Configuration;
using Microsoft.Extensions.Configuration;
namespace RedmineTest
{
    class Program
    {
        public static void Main(string[] args)
        {
            // Redmine 設定
            string host = "host";
            string apiKey = "apiKey";
            var redmineManager = new RedmineManager(host, apiKey);

            // Azure DevOps 設定
            string azureDevOpsUrl = "azure DevOps Url";
            string azureDevOpsPat = "azure DevOps Token";
            VssConnection connection = new VssConnection(new Uri(azureDevOpsUrl), new VssBasicCredential(string.Empty, azureDevOpsPat));
            WorkItemTrackingHttpClient workItemTrackingClient = connection.GetClient<WorkItemTrackingHttpClient>();

            // 取得 Redmine 中的 issue
            IList<Issue> issues = redmineManager.GetObjects<Issue>(new NameValueCollection(){
                 { "status_id", "*"},
                 { "project_id", "tcbbank03"},
                 { "limit", "10"}
             });

            foreach (var issue in issues)
            {
                // 將 Redmine issue 匯入到 Azure DevOps
                var workItem = new JsonPatchDocument(){
                     new JsonPatchOperation()
                     {
                         Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                         Path = "/fields/System.Title",
                         Value = $"#{issue.Id}_{issue.Subject}"
                     },
                     new JsonPatchOperation()
                     {
                         Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                         Path = "/fields/System.Description",
                         Value = issue.Description
                     },
                     new JsonPatchOperation()
                     {
                         Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                         Path = "/fields/System.CreatedDate",
                         Value = issue.CreatedOn
                     },
                     new JsonPatchOperation()
                     {
                         Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                         Path = "/fields/System.AssignedTo",
                         Value = "conte.ma"
                     }
                 };
                workItemTrackingClient.CreateWorkItemAsync(workItem, "TCBBANK", "Issue").Wait();
            }
        }
    }
}