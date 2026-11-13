# 呼叫設定檔（啟動時呼叫程式）

其他語言：[English](launch-config.en.md) | [日本語](launch-config.ja.md)

0.3.0 新增。每個佈局可以有一個呼叫設定檔，列出要呼叫的程式；ThumbDock 啟動後依序執行，並把出現的視窗綁到指定槽位。

**用詞**：「啟動」專指啟動 ThumbDock 本身；ThumbDock 去開其他程式（Chrome、CMD…）叫「呼叫」。功能名稱為「啟動時呼叫程式」，設定檔稱「呼叫設定檔」。

## 啟用方式

1. 系統匣 →「設定…」→ 勾選「允許依呼叫設定檔呼叫程式」（**預設關閉**）。
2. 選擇視窗 →「啟動時執行」→「編輯呼叫設定檔…」：開啟填欄位的編輯表單（0.3.5 起，見下方「編輯表單」）。要直接改 JSON 時，按表單內的「改用記事本編輯（進階）…」，會建立範本並用記事本開啟。
3. 改完存檔。之後：
   - 程式啟動時，會自動執行目前佈局的設定檔。
   - 或手動：選擇視窗 →「啟動時執行」→「現在呼叫這個佈局的程式」。
   - 在系統匣切換佈局時**不會**自動啟動。
4. 命令列 `--no-launch` 可強制停用（類似安全模式），優先於設定。

### 工作模式切換（0.3.6）

一個工作模式（文書、開發、監控…）就是一個佈局，各自有自己的呼叫設定檔。在「設定」勾選「切換佈局時，也依該佈局的呼叫設定檔備妥程式」（預設關，需同時啟用「允許呼叫」）後，從選單切換佈局會：

1. 先載入新佈局，等自動綁定把**已經開著**的視窗接回槽位。
2. 再依新佈局的設定檔，只呼叫**還沒有視窗**的槽位；已開的程式不會重複開，**舊佈局的程式也不會被關閉**。
3. 新佈局沒有設定檔，或某槽位沒列在設定檔裡，就不會呼叫（該縮圖視為不需要啟動）。

要讓「已開的程式」被新佈局認出來，槽位需要有比對規則。第一次由呼叫綁定時，規則會自動產生並存進佈局；之後切換就能辨認。切換時若上一個佈局的呼叫還在進行，會立刻中止。

檔案位置：`config\launch\<佈局名稱>.json`（和佈局檔同樣的檔名規則）。

## 編輯表單（0.3.5）

左側是呼叫清單（順序＝執行順序，可新增、複製、刪除、上移、下移），右側依「程式類型」顯示欄位，最下方即時顯示實際的路徑與參數：

| 程式類型 | 欄位 |
|---|---|
| Chrome／Edge | 帳號設定檔（下拉選單，來自瀏覽器的 Local State，讀不到時可手動輸入）、網址、兩個防遮蔽旗標的勾選、其他旗標 |
| 命令提示字元 (CMD) | 模式（`/k` 保持開啟、`/c` 執行後關閉）、指令 |
| 一般程式／捷徑／批次檔 | 參數 |

共用欄位：槽位、程式路徑（可瀏覽）、工作目錄、完成後等待毫秒數、等視窗秒數、視窗比對規則（沿用「比對規則」視窗）、備註、「測試呼叫這一項」（只開啟程式，不綁槽位）。

- 「程式類型」不寫進檔案：開啟時由 `path`、`args` 判斷；認不得的格式（例如 Brave、`cmd.exe /d /k`）歸為「一般程式」，只有原始參數欄。瀏覽器參數中不認得的旗標會原樣保留在「其他旗標」。
- **存檔會重寫整份 JSON**：手寫的 `//` 註解、`說明`、`_範例` 區塊會消失，舊檔先複製為 `<佈局>.json.bak`。要留註解請寫在每項的 `note`（備註）欄位，它會被保留，執行時忽略。
- 同一個槽位重複、沒填程式路徑時不能儲存；槽位超過佈局槽位數、標題 Regex 無效只會警告。

## 格式

```json
{
  "launch": [
    {
      "slot": 1,
      "path": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
      "args": "--profile-directory=\"Profile 3\" https://example.com",
      "workDir": "",
      "delayMs": 1500,
      "waitSec": 15,
      "match": { "process": "chrome.exe", "titleRegex": "視窗標題的一部分" }
    },
    {
      "slot": 2,
      "path": "cmd.exe",
      "args": "/k cd /d D:\\work && python run.py"
    }
  ]
}
```

| 欄位 | 必填 | 說明 |
|---|---|---|
| `slot` | 是 | 槽位編號（從 1 開始）。同一個槽位只能有一項，重複的會被略過 |
| `path` | 是 | 程式、捷徑（.lnk）或批次檔。可用 `%ProgramFiles%` 這類環境變數，或 PATH 內的名稱（如 `cmd.exe`） |
| `args` | 否 | 啟動參數 |
| `workDir` | 否 | 工作目錄 |
| `delayMs` | 否 | 這一項完成後，等多久再啟動下一項。預設 1000，上限 60000 |
| `waitSec` | 否 | 等程式視窗出現的秒數。預設 15，範圍 1～120 |
| `match` | 否 | 比對規則（`process`、`class`、`titleRegex`），見下方 |
| `note` | 否 | 備註，執行時忽略；編輯表單存檔時會保留 |

JSON 注意事項：
- **路徑的反斜線要寫成兩個**（`C:\\Program Files\\...`），參數裡的雙引號寫成 `\"`。寫錯時會顯示出錯的行號。
- 可以用 `//` 註解，最後一項後面的逗號也可以。
- 範本裡的 `_範例` 區塊會被忽略；要用就把項目搬到 `launch` 陣列。

## 視窗怎麼綁到槽位

| 有沒有 `match` | 做法 |
|---|---|
| 沒有（預設） | 啟動前記下現有視窗，啟動後取**第一個新出現**的視窗綁到槽位（優先選行程名稱與 `path` 相同的）。規則自動產生並存入佈局 |
| 有 | 先把規則寫入該槽位，再等符合規則的視窗出現 |

何時該寫 `match`：同時啟動很多相同程式（例如多個 Chrome 帳號），第一個新視窗可能不是想要的那個，或其他程式剛好也開了視窗。標題要怎麼寫，可以先用「選擇視窗」看實際標題。

## 瀏覽器影片在其他虛擬桌面或被蓋住時停止（監控用途）

Chrome／Edge／Brave 等 Chromium 瀏覽器內建「視窗遮蔽偵測」：視窗在其他虛擬桌面、或被其他視窗完全蓋住時，會停止繪製（官方文件：Chromium 的 windows_native_window_occlusion_tracking.md）。縮圖因此凍結或變黑。

實測下面這個組合**有效**（Chrome，其他虛擬桌面的 YouTube 持續播放）：

```json
{
  "slot": 3,
  "path": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
  "args": "--disable-features=CalculateNativeWinOcclusion --profile-directory=\"Profile 10\" https://www.youtube.com/",
  "match": { "process": "chrome.exe", "titleRegex": "YouTube" }
}
```

注意：
- 2026-11-07 補測：**不需要獨立的 `--user-data-dir`**。直接用預設資料夾加旗標與 `--profile-directory="Profile 10"` 指定帳號，其他虛擬桌面與最大化情境下縮圖都持續即時（含影音與 JS 動態內容）。下方範例改用這個寫法。獨立 `--user-data-dir` 也有效，但不會出現多帳號選擇、需重新登入。
- 獨立的 `--user-data-dir` 是全新的設定檔，需要重新登入帳號，不會有原本的書籤與登入狀態。每個要獨立旗標的瀏覽器視窗各用一個資料夾（chrome-a、chrome-b…）。
- 以下兩個旗標在補測中**都有效**（Chrome，各自搭配 `--profile-directory`）：`--disable-features=CalculateNativeWinOcclusion`、`--disable-backgrounding-occluded-windows`。無痕模式、關閉擴充功能沒有效果。
- 這個旗標是社群回報的作法，**Chromium 官方文件沒有記載**，未來瀏覽器改版可能失效。停用遮蔽偵測會增加 CPU 與耗電。
- Brave 實測**不適用**這些旗標，縮圖仍會變黑。

## 行為與限制

- 槽位已經有視窗（包含剛由規則自動綁上）時，**不會再啟動**該項，避免重複開啟。
- 程式以目前使用者身分啟動，不會提升權限；不要在設定檔放密碼。
- CMD：`cmd.exe` 搭配 `/k 指令`（執行後保持開啟）。Windows 11 的 CMD 視窗可能由 `WindowsTerminal.exe` 承載，這是正常的，會依視窗綁定。
- 不支援 Store／UWP 應用程式。
- 每次啟動都會寫入 `logs\thumbdock.log`（路徑、參數、綁定結果、逾時），回報問題時請附上。
- 指定的槽位不存在（例如只有 4 個槽位卻寫 `slot: 6`）時會略過並在 log 記錄；請先在設定調整槽位數量。

## Chrome 旗標的注意事項（2026-11-08 實測確認）

- `--disable-features=CalculateNativeWinOcclusion` 或 `--disable-backgrounding-occluded-windows` 只需要帶在**第一個啟動的 Chrome 行程**，之後同一個使用者資料夾下用其他 `--profile-directory` 開的視窗都會沿用。
- 反過來說：Chrome 已經在執行（含背景行程）時，後來帶的旗標會被忽略。呼叫順序請讓帶旗標的那一項最先啟動，或先完全結束所有 chrome.exe。
- 確認方式：PowerShell 執行 `Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | ? {$_.CommandLine -notmatch '--type='} | select ProcessId,CommandLine`，主程序的命令列要看得到旗標。
