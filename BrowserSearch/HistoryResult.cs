using System;
using Wox.Plugin;

namespace BrowserSearch;

internal record HistoryResult
{
    public required string URL { get; init; }
    public required string Title { get; init; }
    public required string IcoPath { get; init; }
    public required Func<ActionContext, bool> Action { get; init; }
    // Only set for bookmarks
    public string? Folder { get; init; }

    public Result ToResult()
    {
        return new Result
        {
            Title = Title,
            SubTitle = Folder is null ? URL : $"{Folder} - {URL}",
            QueryTextDisplay = URL,
            IcoPath = IcoPath,
            Action = Action
        };
    }
}
