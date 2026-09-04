using System.Collections.Generic;

namespace SimpleGit11.Plugin.Ssh.Services;

public interface ISshConnectionProfileStore
{
    IReadOnlyList<SshConnectionProfile> Load();
    void Upsert(SshConnectionProfile profile);
    void Delete(string profileId);
}
