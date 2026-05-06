using System.Collections.ObjectModel;

namespace Cress.Studio.ViewModels;

public sealed class ExplorerNodeViewModel
{
    public string DisplayName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string? Path { get; init; }
    public object? Payload { get; init; }
    public ObservableCollection<ExplorerNodeViewModel> Children { get; } = [];
}
