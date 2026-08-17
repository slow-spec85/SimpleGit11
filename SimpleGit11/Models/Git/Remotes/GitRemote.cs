namespace SimpleGit11.Models;

public sealed class GitRemote
{
    public GitRemote(string name, string fetchUrl, string pushUrl)
    {
        Name = name;
        FetchUrl = fetchUrl;
        PushUrl = pushUrl;
    }

    public string Name { get; }

    public string FetchUrl { get; }

    public string PushUrl { get; }

    public string DisplayUrl => string.IsNullOrWhiteSpace(FetchUrl) ? PushUrl : FetchUrl;
}
