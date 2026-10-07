// Parrot for Windows. Derived from Parrot (GPL-3.0), AskPageView.swift / AskEngine.swift.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Parrot.App.Services;
using Parrot.Core.Ask;
using Parrot.Core.Diagnostics;
using Parrot.Core.Models;

namespace Parrot.App.ViewModels;

public sealed class AskSourceItem
{
    public required string Header { get; init; }
    public required string Text { get; init; }
    public required Guid MeetingId { get; init; }
}

public sealed class AskExchange
{
    public required string Question { get; init; }
    public required string Answer { get; init; }
    public string? Note { get; init; }
    public List<AskSourceItem> Sources { get; init; } = new();
    public string? Model { get; init; }
}

/// Ask Parrot: questions about past meetings, answered with citations.
public sealed partial class AskViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<Guid> _openMeeting;

    public ObservableCollection<AskExchange> Exchanges { get; } = new();

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AskCommand))] private string _question = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AskCommand))] private bool _isBusy;
    [ObservableProperty] private string? _error;

    public AskViewModel(AppServices services, Action<Guid> openMeeting)
    {
        _services = services;
        _openMeeting = openMeeting;
    }

    private bool CanAsk() => !IsBusy && !string.IsNullOrWhiteSpace(Question);

    [RelayCommand(CanExecute = nameof(CanAsk))]
    private async Task AskAsync()
    {
        var q = Question.Trim();
        IsBusy = true;
        Error = null;
        try
        {
            var meetings = await Task.Run(() => _services.Meetings.LoadAll());
            var provider = _services.ReportsProvider();
            var answer = await AskEngine.AskAsync(provider, meetings, q);
            var byId = meetings.ToDictionary(m => m.Id);
            var sources = answer.Sources.Select(s =>
            {
                var m = byId.GetValueOrDefault(s.MeetingId);
                var header = (m?.Title ?? "Meeting") + (s.IsReport ? " · report" : " · " + TimeFormat.Stamp(s.Start));
                return new AskSourceItem { Header = header, Text = s.Text, MeetingId = s.MeetingId };
            }).ToList();
            Exchanges.Insert(0, new AskExchange
            {
                Question = q,
                Answer = answer.AnsweredByAI ? answer.Text : (answer.Sources.Count == 0 ? "Nothing in your meetings matched." : "Closest excerpts below."),
                Note = answer.Note,
                Sources = sources,
                Model = answer.AnsweredByAI ? provider.DisplayName : null,
            });
            Question = "";
        }
        catch (Exception e)
        {
            Log.Error("Ask failed", e);
            Error = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenSource(AskSourceItem? item)
    {
        if (item != null) _openMeeting(item.MeetingId);
    }
}
