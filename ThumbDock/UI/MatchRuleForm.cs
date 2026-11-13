using ThumbDock.Core;
using ThumbDock.Layout;

namespace ThumbDock.UI;

/// <summary>Edits a slot's match rule (FR-5) with a live preview of the windows it matches.</summary>
internal sealed class MatchRuleForm : Form
{
    private readonly TextBox _process = new() { Dock = DockStyle.Fill };
    private readonly TextBox _class = new() { Dock = DockStyle.Fill };
    private readonly TextBox _title = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly ListBox _preview = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly List<WindowInfo> _windows;

    public MatchRuleForm(int slotNumber, MatchRule? rule, List<WindowInfo> windows)
    {
        _windows = windows;
        Text = Loc.T("槽位 {0} 的比對規則", slotNumber);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        _process.Text = rule?.Process ?? "";
        _class.Text = rule?.Class ?? "";
        _title.Text = rule?.TitleRegex ?? "";
        Controls.Add(BuildLayout());
        foreach (var box in new[] { _process, _class, _title }) box.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    /// <summary>The edited rule; null when every field is empty (the slot will not auto-bind).</summary>
    public MatchRule? Rule
    {
        get
        {
            var rule = new MatchRule(_process.Text.Trim(), _class.Text.Trim(), _title.Text.Trim());
            return rule.IsEmpty ? null : rule;
        }
    }

    private TableLayoutPanel BuildLayout()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(table, Loc.T("行程名稱"), _process);
        AddRow(table, Loc.T("視窗類別"), _class);
        AddRow(table, Loc.T("標題 (Regex)"), _title);
        table.Controls.Add(new Label { Text = Loc.T("空白的欄位代表不限制。重新開機後，ThumbDock 會用這個規則自動找回視窗。"), AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 0, 3);
        table.SetColumnSpan(table.GetControlFromPosition(0, 3)!, 2);
        table.Controls.Add(_status, 0, 4);
        table.SetColumnSpan(_status, 2);
        table.Controls.Add(_preview, 0, 5);
        table.SetColumnSpan(_preview, 2);
        table.Controls.Add(BuildButtons(), 0, 6);
        table.SetColumnSpan(table.GetControlFromPosition(0, 6)!, 2);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return table;
    }

    private static void AddRow(TableLayoutPanel table, string label, Control field)
    {
        int row = table.Controls.Count / 2;
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(field, 1, row);
    }

    private FlowLayoutPanel BuildButtons()
    {
        var ok = new Button { Text = Loc.T("確定"), DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = Loc.T("取消"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var clear = new Button { Text = Loc.T("清除規則"), AutoSize = true };
        clear.Click += (_, _) => { _process.Text = _class.Text = _title.Text = ""; };
        AcceptButton = ok;
        CancelButton = cancel;
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        panel.Controls.AddRange([cancel, ok, clear]);
        return panel;
    }

    private void UpdatePreview()
    {
        string? error = WindowMatcher.ValidateRegex(_title.Text.Trim());
        _preview.BeginUpdate();
        _preview.Items.Clear();
        if (error is null && Rule is { } rule)
            foreach (var w in _windows.Where(w => WindowMatcher.IsMatch(rule, w))) _preview.Items.Add($"{w.Title}  —  {w.ProcessName}");
        _preview.EndUpdate();
        _status.ForeColor = error is null ? SystemColors.ControlText : Color.Firebrick;
        _status.Text = error is not null ? Loc.T("標題 Regex 無效：{0}", error)
            : Rule is null ? Loc.T("規則是空的：這個槽位不會自動綁定。")
            : Loc.T("目前符合的視窗：{0} 個（有多個時綁定第一個）", _preview.Items.Count);
    }
}
