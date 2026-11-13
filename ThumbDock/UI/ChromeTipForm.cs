using ThumbDock.Core;

namespace ThumbDock.UI;

/// <summary>First-use tip: Chrome stops painting covered / other-desktop windows unless started with two flags.</summary>
internal sealed class ChromeTipForm : Form
{
    private const string Flags = "--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows";
    private readonly CheckBox _dontShow = new() { Text = Loc.T("以後不顯示"), AutoSize = true };

    public ChromeTipForm()
    {
        Text = Loc.T("ThumbDock {0} － 使用提醒", AppInfo.Version);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;
        ClientSize = new Size(560, 330);

        var text = new Label
        {
            Left = 14, Top = 12, Width = 532, Height = 150, AutoSize = false,
            Text = Loc.T("如果要用縮圖監看 Chrome，被其他視窗蓋住或在其他虛擬桌面的 Chrome，縮圖可能會停止更新（變黑或停格）。\n\n原因是 Chrome 會暫停繪製被遮蔽的視窗，不是 ThumbDock 的問題。解法是用下列參數啟動 Chrome（兩個擇一即可，下方一起複製）：\n\n注意：參數只對「第一個啟動的 Chrome 行程」有效。請先完全結束所有 chrome.exe（含系統匣背景行程），再帶參數啟動；可到 chrome://version 的「命令列」確認。"),
        };
        var box = new TextBox { Left = 14, Top = 170, Width = 532, Height = 54, Multiline = true, ReadOnly = true, Text = Flags };
        var copy = new Button { Text = Loc.T("複製參數"), Left = 14, Top = 232, Width = 100 };
        copy.Click += (_, _) => { try { Clipboard.SetText(Flags); } catch (System.Runtime.InteropServices.ExternalException) { } };
        var more = new Label { Left = 14, Top = 266, Width = 532, Height = 20, Text = Loc.T("也可寫在「呼叫設定檔」的 args（詳見文件 launch-config.md）。"), ForeColor = Color.DimGray };
        _dontShow.Location = new Point(14, 296);
        var ok = new Button { Text = Loc.T("知道了"), Left = 446, Top = 292, Width = 100, DialogResult = DialogResult.OK };
        AcceptButton = ok;
        Controls.AddRange([text, box, copy, more, _dontShow, ok]);
    }

    public bool DontShowAgain => _dontShow.Checked;
}
