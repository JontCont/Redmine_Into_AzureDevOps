using System.Collections.Specialized;
using Redmine.Net.Api;
using Redmine.Net.Api.Types;

namespace redmineApi;

public class RedmineFactory : RedmineManager
{
    static readonly string RedmineApiKey = "KEY";
    static readonly string RedmineHost   = "HOST";
    private string? Project { get; set; }

    public RedmineFactory() : base(RedmineHost, RedmineApiKey)
    {
    }

    public RedmineFactory(string? project) : base(RedmineHost, RedmineApiKey)
    {
        if (string.IsNullOrWhiteSpace(project)) throw new ArgumentNullException(nameof(project));
        this.Project = project;
    }

    public IList<Issue> GetIssues(string limit = "10")
    {
        return this.GetObjects<Issue>(new NameValueCollection()
        {
            { "status_id", "*" },
            { "project_id", this.Project },
            { "limit", limit }
        });
    }
    
    public IList<IssueStatus> GetStatuses()
    {
        return this.GetObjects<IssueStatus>();
    }
    
}