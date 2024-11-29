using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Newtonsoft.Json;

namespace redmineApi;

public class AzureDevopsFactory : VssConnection
{
    static readonly Uri BaseUrl = new("URL");

    private const string AzureDevopPat =
        "KEY";

    static readonly VssCredentials VssCredentials = new VssBasicCredential(string.Empty, AzureDevopPat);
    static VssConnection _connection = null!;
    static string? _projectName;

    public AzureDevopsFactory(string projectName) : base(BaseUrl, VssCredentials)
    {
        if (string.IsNullOrWhiteSpace((projectName))) throw new ArgumentNullException(nameof(projectName));
        _projectName = projectName;
        _connection = new VssConnection(BaseUrl, VssCredentials);
    }

    public AzureDevopsFactory(string projectName, VssHttpRequestSettings settings) : base(BaseUrl, VssCredentials,
        settings)
    {
        if (string.IsNullOrWhiteSpace((projectName))) throw new ArgumentNullException(nameof(projectName));
        _projectName = projectName;
        _connection = new VssConnection(BaseUrl, VssCredentials);
    }

    public AzureDevopsFactory(string projectName, VssHttpMessageHandler innerHandler,
        IEnumerable<DelegatingHandler> delegatingHandlers) :
        base(BaseUrl, innerHandler, delegatingHandlers)
    {
        if (string.IsNullOrWhiteSpace((projectName))) throw new ArgumentNullException(nameof(projectName));
        _projectName = projectName;
        _connection = new VssConnection(BaseUrl, VssCredentials);
    }

    public async Task<TeamProjectReference> GetProject()
    {
        var projectClient = _connection!.GetClient<ProjectHttpClient>();
        var project = await projectClient.GetProject(_projectName!).ConfigureAwait(false);
        return project;
    }

    public async Task GetWorkItems()
    {
        using var workItemClient = _connection.GetClient<WorkItemTrackingHttpClient>();

        var wiql = new Wiql
        {
            Query = $@"Select [System.Id] From WorkItems "
        };

        var worItemsIds =  await workItemClient.QueryByWiqlAsync(wiql, _projectName).ConfigureAwait(false);
        var list = await workItemClient.GetWorkItemsAsync(worItemsIds.WorkItems.Select(x=>x.Id).ToArray());
        foreach (var item in list)
        {
            Console.WriteLine($"#{item.Id} - [{item.Fields["System.WorkItemType"]}] - {item.Fields["System.Title"]} - { item.Fields["System.State"] }");;
        }
    }
}