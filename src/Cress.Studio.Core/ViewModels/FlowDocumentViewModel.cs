using System.Collections.ObjectModel;
using Cress.Studio.Services;

namespace Cress.Studio.ViewModels;

public sealed class FlowDocumentViewModel : ObservableObject
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string? _capabilityId;
    private string? _summary;
    private string? _status;
    private string _tagsText = string.Empty;
    private string _sourceText = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string? CapabilityId
    {
        get => _capabilityId;
        set => SetProperty(ref _capabilityId, value);
    }

    public string? Summary
    {
        get => _summary;
        set => SetProperty(ref _summary, value);
    }

    public string? Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string TagsText
    {
        get => _tagsText;
        set => SetProperty(ref _tagsText, value);
    }

    public string SourceText
    {
        get => _sourceText;
        set => SetProperty(ref _sourceText, value);
    }

    public ObservableCollection<EditableFixtureRow> Fixtures { get; } = [];
    public ObservableCollection<EditableExecutableRow> Actions { get; } = [];
    public ObservableCollection<EditableExecutableRow> Expectations { get; } = [];

    public static FlowDocumentViewModel FromDocument(FlowEditorDocument document)
    {
        var viewModel = new FlowDocumentViewModel
        {
            FilePath = document.FilePath,
            Id = document.Id,
            Name = document.Name,
            CapabilityId = document.CapabilityId,
            Summary = document.Summary,
            Status = document.Status,
            TagsText = document.TagsText,
            SourceText = document.SourceText
        };

        foreach (var fixture in document.Fixtures)
        {
            viewModel.Fixtures.Add(new EditableFixtureRow
            {
                Alias = fixture.Alias,
                Use = fixture.Use,
                Source = fixture.Source,
                For = fixture.For
            });
        }

        foreach (var action in document.Actions)
        {
            viewModel.Actions.Add(new EditableExecutableRow
            {
                Name = action.Name,
                InputsText = action.InputsText
            });
        }

        foreach (var expectation in document.Expectations)
        {
            viewModel.Expectations.Add(new EditableExecutableRow
            {
                Name = expectation.Name,
                InputsText = expectation.InputsText
            });
        }

        return viewModel;
    }

    public FlowEditorDocument ToDocument()
        => new()
        {
            FilePath = FilePath,
            Id = Id,
            Name = Name,
            CapabilityId = string.IsNullOrWhiteSpace(CapabilityId) ? null : CapabilityId,
            Summary = string.IsNullOrWhiteSpace(Summary) ? null : Summary,
            Status = string.IsNullOrWhiteSpace(Status) ? null : Status,
            TagsText = TagsText,
            SourceText = SourceText,
            Fixtures = Fixtures.Where(row => !string.IsNullOrWhiteSpace(row.Alias) || !string.IsNullOrWhiteSpace(row.Use) || !string.IsNullOrWhiteSpace(row.Source))
                .Select(row => new EditableFixture
                {
                    Alias = row.Alias,
                    Use = row.Use,
                    Source = row.Source,
                    For = row.For
                })
                .ToList(),
            Actions = Actions.Where(row => !string.IsNullOrWhiteSpace(row.Name))
                .Select(row => new EditableExecutable
                {
                    Name = row.Name,
                    InputsText = row.InputsText
                })
                .ToList(),
            Expectations = Expectations.Where(row => !string.IsNullOrWhiteSpace(row.Name))
                .Select(row => new EditableExecutable
                {
                    Name = row.Name,
                    InputsText = row.InputsText
                })
                .ToList()
        };

    public sealed class EditableFixtureRow : ObservableObject
    {
        private string _alias = string.Empty;
        private string? _use;
        private string? _source;
        private string? _for;

        public string Alias
        {
            get => _alias;
            set => SetProperty(ref _alias, value);
        }

        public string? Use
        {
            get => _use;
            set => SetProperty(ref _use, value);
        }

        public string? Source
        {
            get => _source;
            set => SetProperty(ref _source, value);
        }

        public string? For
        {
            get => _for;
            set => SetProperty(ref _for, value);
        }
    }

    public sealed class EditableExecutableRow : ObservableObject
    {
        private string _name = string.Empty;
        private string _inputsText = string.Empty;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string InputsText
        {
            get => _inputsText;
            set => SetProperty(ref _inputsText, value);
        }
    }
}
