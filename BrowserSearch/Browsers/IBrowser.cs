using System.Collections.Generic;
using Wox.Plugin;

namespace BrowserSearch.Browsers
{
    internal interface IBrowser
    {
        void Init();
        List<HistoryResult> GetHistory();
        List<HistoryResult> GetBookmarks();
        int CalculateExtraScore(string query, string title, string url);
    }
}
