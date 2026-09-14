using SimpleGit11.Models;

namespace SimpleGit11.Messages;

public sealed record RepositoryChangedMessage(RepositoryInfo? Repository);
