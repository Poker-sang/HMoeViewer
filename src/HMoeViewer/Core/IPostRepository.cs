using System.Collections.Generic;
using System.Threading.Tasks;
using HMoeData.Models;

namespace HMoeViewer.Core;

public interface IPostRepository
{
    Task<IReadOnlyList<Post>> LoadLatestAsync(string databasePath);

    Task SaveAsync(string databasePath, IReadOnlyList<PostSelection> selections);
}
