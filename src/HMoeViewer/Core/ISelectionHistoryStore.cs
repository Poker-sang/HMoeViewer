using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HMoeViewer.Core;

public interface ISelectionHistoryStore
{
    Task<IReadOnlyList<SelectionChange>> ReadAsync(SelectionBatch batch, CancellationToken token = default);

    Task AppendAsync(SelectionBatch batch, IReadOnlyList<SelectionChange> changes, CancellationToken token = default);
}
