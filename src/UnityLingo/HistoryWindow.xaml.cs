using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UnityLingo.Core;

namespace UnityLingo;

public partial class HistoryWindow : Window
{
    private readonly HistoryStore history;
    private readonly int limit;
    private int page;
    private string query = "";
    public HistoryEntry? SelectedEntry { get; private set; }
    public HistoryWindow(HistoryStore history, int limit) { this.history = history; this.limit = limit; InitializeComponent(); Refresh(); }
    private void Refresh()
    {
        try
        {
            var rows = history.Search(query, page * 100, 101);
            if (rows.Count == 0 && page > 0) { page--; Refresh(); return; }
            Entries.ItemsSource = rows.Take(100).ToArray(); Next.IsEnabled = rows.Count > 100; Previous.IsEnabled = page > 0;
            Status.Text = $"共 {history.Count()} 条 · 保存上限 {limit} 条（可在设置中修改） · 第 {page + 1} 页";
            Preview.Text = rows.Count == 0 ? "没有匹配的历史记录。" : "选择一条记录查看详情。";
        }
        catch { Status.Text = "读取历史失败，请检查本地数据库。"; }
    }
    private void Search_Click(object sender, RoutedEventArgs e) { page = 0; query = Query.Text; Refresh(); }
    private void Query_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; Search_Click(sender, e); } }
    private void Previous_Click(object sender, RoutedEventArgs e) { if (page > 0) page--; Refresh(); }
    private void Next_Click(object sender, RoutedEventArgs e) { page++; Refresh(); }
    private void Entry_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (Entries.SelectedItem is not HistoryEntry entry) return;
        var output = entry.Result.Naming is { } n
            ? string.Join("\n\n", n.Alternatives.Prepend(n.Recommended).Select((x, i) => $"{(i == 0 ? "推荐" : "备选")}：{x.Name}\n{x.Explanation}"))
            : entry.Result.Text;
        Preview.Text = $"{entry.CreatedAt.ToLocalTime():g}\n{entry.Scenario} · {entry.Model}\n\n输入\n{entry.Input}\n\n上下文\n{entry.Context}\n\n结果\n{output}";
    }
    private void Open_Click(object sender, RoutedEventArgs e) { if (Entries.SelectedItem is HistoryEntry entry) { SelectedEntry = entry; DialogResult = true; } }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is not HistoryEntry entry) return;
        try { Clipboard.SetText(entry.Result.Naming?.Recommended.Name ?? entry.Result.Text); Status.Text = "已复制结果（命名模式仅复制推荐名）。"; }
        catch { Status.Text = "剪贴板暂时不可用，请稍后重试。"; }
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is not HistoryEntry entry) return;
        if (MessageBox.Show(this, "永久删除选中的这条记录？", "删除记录", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { history.Delete(entry.Id); Refresh(); } catch { Status.Text = "删除失败，请检查数据库是否可写。"; }
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "永久删除全部历史记录？此操作无法撤销。", "清空历史", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { history.Clear(); page = 0; Refresh(); } catch { Status.Text = "清空失败，请检查数据库是否可写。"; }
    }
}
