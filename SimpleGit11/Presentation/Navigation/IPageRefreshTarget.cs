using System.Threading.Tasks;

namespace SimpleGit11.Presentation.Navigation;

public interface IPageRefreshTarget
{
    Task RefreshAsync();
}
