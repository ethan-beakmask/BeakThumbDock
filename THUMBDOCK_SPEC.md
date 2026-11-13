# BeakThumbDock 開發規格

正式名稱為 BeakThumbDock（2026-11-13 定案）。程式、執行檔、命名空間與下文沿用開發期間的名稱 ThumbDock。

給 Claude Code 的開發文件。請先完整讀完，再從「階段 0」開始做，每個階段通過驗收後才進下一階段。

## 1. 目標

做一個 Windows 免安裝工具，把多個程式視窗（含其他虛擬桌面上的視窗）以**即時縮圖**排在固定的「縮圖區」，使用者點任一縮圖，該視窗就出現在「操作區」，縮圖本身不消失。佈局概念等同 PowerPoint 編輯畫面：一側是縮圖列，其餘是主要操作區。

參考對象是 OnTopReplica（只參考它用 DWM Thumbnail API 的方式，不沿用程式碼）。它不合用的三點就是本專案的核心需求：

- 縮圖只能放四角與中間 → 本專案要能自訂並儲存位置
- 點選後縮圖消失 → 本專案縮圖常駐
- 一個視窗只有一個縮圖 → 本專案一個面板管理多個縮圖

## 2. 名詞

| 名詞 | 定義 |
|---|---|
| 來源視窗 | 被監看的其他程式頂層視窗 |
| 縮圖區 | 本工具佔用的螢幕區域，裡面放縮圖 |
| 操作區 | 縮圖區以外、使用者實際操作來源視窗的區域 |
| 焦點螢幕 | 雙螢幕時被指定為「沒有縮圖、只當操作區」的螢幕 |
| 槽位 (slot) | 縮圖區裡一個有位置與大小的格子，可綁定一個來源視窗 |
| 佈局檔 (layout) | 一組槽位定義 + 綁定規則 + 螢幕配置，可命名、儲存、載入 |

## 3. 佈局模式

### 3.1 單螢幕

```
+--------+---------------------------------+
| 縮圖區 |                                 |
| [ 1 ]  |                                 |
| [ 2 ]  |            操作區               |
| [ 3 ]  |                                 |
| [ 4 ]  |                                 |
+--------+---------------------------------+
```

- 縮圖區預設靠左直排，可改靠右／上／下，寬度（或高度）可調。
- **縮圖區不可被來源視窗蓋住**，來源視窗最大化時也只能佔滿操作區。實作方式：把縮圖區註冊成 AppBar（`SHAppBarMessage`），讓系統把該區域從工作區 (work area) 扣掉，行為等同工作列。

### 3.1.1 單螢幕 fill：縮圖牆（0.4.0 新增）

單螢幕也可以把縮圖區型態設為 `fill`，稱為「縮圖牆」，情境是監看多個畫面：縮圖排成陣列蓋滿整個螢幕，點某個縮圖，該視窗在同一螢幕顯示在牆之上（預設最大化）；按快速鍵回到牆。

- 牆不是 AppBar，也不是常駐最上層（工作列仍在牆之上）。需要時才把牆拉到最上層，且不啟用它。
- 保留 `WS_EX_TOOLWINDOW`、`WS_EX_NOACTIVATE` 與 `MA_NOACTIVATE`。實測 `TOOLWINDOW` 讓 Chrome 不把牆視為遮蔽物，不需要旗標、不需要留縫（MVP 驗證，非 Chromium 文件記載）。
- 快速鍵「顯示／隱藏縮圖區」在牆模式下改為：牆不在最上層時回到牆；牆已在最上層時，回到上一個顯示的視窗。
- 回到牆時，會把「牆替它最大化」的視窗還原成原本的位置與大小（`restoreOnReturn`，預設開）。原本就最大化的視窗不動；原本最小化的視窗還原為一般狀態，不再最小化。目的是避免被最大化視窗蓋住的瀏覽器停止繪製，縮圖凍結。
- 來源視窗在其他虛擬桌面時，Chrome 仍須加啟動旗標（見 launch-config.md）。
- 雙螢幕佈局遇到只剩一個螢幕時，仍退回單螢幕 `dock`，避免牆意外蓋住唯一的螢幕。

### 3.2 雙螢幕（或更多）

```
   螢幕 A（縮圖螢幕）              螢幕 B（焦點螢幕）
+----------------------+     +----------------------+
| [ 1 ]  [ 2 ]  [ 3 ]  |     |                      |
| [ 4 ]  [ 5 ]  [ 6 ]  |     |        操作區        |
|                      |     |                      |
+----------------------+     +----------------------+
```

- 使用者指定哪一個螢幕是焦點螢幕，該螢幕完全沒有縮圖。
- 縮圖螢幕上的縮圖區有兩種型態，由設定決定：
  - `dock`：跟單螢幕一樣是一條 AppBar，該螢幕剩餘空間仍可放其他視窗。
  - `fill`：縮圖區佔滿整個螢幕，槽位可在整個螢幕內自由擺放。
- 三個以上螢幕時，一個焦點螢幕、一個縮圖螢幕，其餘不管。

## 4. 功能需求

### FR-1 視窗列舉與選取
- 列出所有可監看的頂層視窗，**包含其他虛擬桌面上的視窗**。
- 過濾條件：可見、有標題、非 tool window、非本工具自己的視窗。
- 注意：其他虛擬桌面的視窗會被標記 `DWMWA_CLOAKED`（值為 `DWM_CLOAKED_SHELL`），不可因此濾掉。請用 `IVirtualDesktopManager.GetWindowDesktopId` 判斷它是否屬於某個桌面，藉此與「被暫停的 UWP 視窗」區分。
- 清單顯示：圖示、標題、行程名稱、所在虛擬桌面（能取得的話）。

### FR-2 即時縮圖
- 每個已綁定的槽位用 `DwmRegisterThumbnail` 註冊一個縮圖，畫在縮圖區視窗上。
- 縮圖維持來源視窗長寬比（letterbox），用 `DwmQueryThumbnailSourceSize` 取得來源尺寸。
- 來源視窗大小改變時要重算縮圖矩形。
- 槽位下方或角落顯示標題文字（可關閉）。

### FR-3 點選切換
- 左鍵點縮圖 → 把該來源視窗帶到操作區並取得焦點，**縮圖保持註冊、不消失**。
- 帶到操作區的方式由設定 `placeOnActivate` 決定：
  - `maximize`（預設）：移到焦點螢幕（單螢幕則是唯一螢幕）後最大化。因為有 AppBar，最大化不會蓋到縮圖區。
  - `fit`：不最大化，改成把視窗尺寸設成操作區大小。
  - `none`：只啟用，不動位置與大小。
- 來源視窗若在其他虛擬桌面，由設定 `crossDesktop` 決定：
  - `switch`（預設）：直接啟用，讓 Windows 自動切到該桌面。
  - `pull`：把視窗搬到目前桌面（階段 3 才做，見風險 R-3）。
- 上一個在操作區的視窗**不可被最小化**，只讓它被蓋住即可（最小化後縮圖會停止更新）。
- 目前在前景的來源視窗，其槽位要有醒目外框。用 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` 追蹤。

### FR-4 自訂槽位位置
- 「編輯模式」可切換。編輯模式下可拖曳、縮放、新增、刪除槽位，有格線吸附。
- 非編輯模式下槽位鎖定，點擊只會觸發 FR-3。
- 槽位座標以**縮圖區的相對比例**（0.0–1.0）儲存，換解析度或 DPI 後仍可用。

### FR-5 佈局檔儲存與載入
- 佈局檔可命名、另存、載入、刪除，啟動時自動載入上次使用的佈局。
- 因為 HWND 每次開機都不同，槽位用**比對規則**綁定來源視窗：行程名稱 + 視窗類別 + 標題（可用 regex）。
- 載入佈局時依規則自動尋找視窗；找不到的槽位顯示「等待中」，之後視窗出現時自動綁上（用 `EVENT_OBJECT_SHOW` 或定時掃描）。
- 來源視窗關閉時，槽位回到「等待中」，不可當掉。

### FR-6 多螢幕與 DPI
- 用 `EnumDisplayMonitors` / `GetMonitorInfo` 取得螢幕，佈局檔記錄螢幕的裝置名稱與解析度。
- 載入時若螢幕配置不符（例如筆電拔掉外接螢幕），自動退回單螢幕模式並提示。
- 處理 `WM_DISPLAYCHANGE` 與 `WM_DPICHANGED`。
- App manifest 宣告 `PerMonitorV2`。

### FR-7 操作介面
- 系統匣圖示：切換佈局、進入編輯模式、設定、結束。
- 縮圖區右鍵選單：指定來源視窗、清除槽位、編輯比對規則。
- 全域快速鍵（可設定）：顯示／隱藏縮圖區、切換到第 N 個槽位。

### FR-8 免安裝
- 單一 exe，不寫登錄檔，不需要系統管理員權限。
- 設定與佈局檔放在 exe 同一層的 `config/` 資料夾。

## 5. 技術選型

- 語言與框架：C#、WinForms、.NET 10（LTS）。開工前請先確認當下的 LTS 版本，以最新 LTS 為準。
- Win32 呼叫：P/Invoke，可用 `Microsoft.Windows.CsWin32` 產生簽章，避免手寫錯誤。
- 設定格式：JSON（`System.Text.Json`）。
- 不使用第三方 UI 套件，不引入超過兩年沒更新的相依套件。

## 6. 專案結構

每個檔案只負責一件事，函式保持短小，複雜流程拆成子函式呼叫。

```
ThumbDock/
  ThumbDock.csproj
  app.manifest                  PerMonitorV2 DPI
  Program.cs                    進入點、單一執行個體
  Native/
    Dwm.cs                      DWM thumbnail 與 cloaked 屬性
    User32.cs                   視窗列舉、定位、前景、事件 hook
    AppBarNative.cs             SHAppBarMessage
    VirtualDesktop.cs           IVirtualDesktopManager（只用官方介面）
  Core/
    WindowInfo.cs               來源視窗資料模型
    WindowEnumerator.cs         FR-1
    WindowMatcher.cs            比對規則 → HWND
    ThumbnailHandle.cs          單一縮圖的註冊、更新、釋放
    WindowActivator.cs          FR-3
    MonitorService.cs           FR-6
    ForegroundTracker.cs        前景視窗追蹤
  Layout/
    LayoutModel.cs              佈局檔資料模型
    LayoutStore.cs              讀寫 JSON
    SlotGeometry.cs             相對座標 ↔ 像素
  UI/
    DockForm.cs                 縮圖區視窗（AppBar 或 fill）
    SlotControl.cs              單一槽位的繪製與滑鼠事件
    EditModeController.cs       FR-4
    WindowPickerForm.cs         選來源視窗
    SettingsForm.cs
    TrayIcon.cs
    HotkeyService.cs
  tests/
    ThumbDock.Tests/            純邏輯的單元測試
```

## 7. 佈局檔格式

```json
{
  "name": "監控台",
  "mode": "dual",
  "focusMonitor": "\\\\.\\DISPLAY1",
  "thumbMonitor": "\\\\.\\DISPLAY2",
  "thumbZone": { "type": "dock", "edge": "left", "sizePx": 320 },
  "placeOnActivate": "maximize",
  "crossDesktop": "switch",
  "showTitles": true,
  "slots": [
    {
      "id": 1,
      "rect": { "x": 0.05, "y": 0.02, "w": 0.90, "h": 0.18 },
      "match": { "process": "chrome.exe", "class": "", "titleRegex": "Dashboard" }
    }
  ]
}
```

- 單螢幕時 `mode` 為 `single`，`focusMonitor` 與 `thumbMonitor` 相同，`thumbZone.type` 可以是 `dock` 或 `fill`（0.4.0 起；單螢幕 `fill` 即縮圖牆，見 3.1.1）。佈局另有 `restoreOnReturn`（預設 `true`）。
- `rect` 是相對於縮圖區的比例。

## 8. 開發階段與驗收

### 階段 0：技術驗證（先做，結果寫進 `docs/internal/spike-results.md`）

用最小的測試程式在實機上確認以下假設，任何一項不成立就停下來回報，不要自行換方案：

| 編號 | 要確認的事 |
|---|---|
| S-1 | 位於其他虛擬桌面的視窗，DWM 縮圖是否持續即時更新 |
| S-2 | AppBar 視窗是否在所有虛擬桌面都看得到；若否，加 `WS_EX_TOOLWINDOW` 是否可行 |
| S-3 | AppBar 扣掉的工作區，在每個虛擬桌面是否都生效 |
| S-4 | 從縮圖區點擊後呼叫 `SetForegroundWindow` 啟用其他桌面的視窗，是否穩定切過去 |
| S-5 | 以一般權限執行時，對系統管理員權限的視窗能否顯示縮圖、能否啟用與移動 |
| S-6 | 被完全遮住（但未最小化）的視窗，縮圖是否持續更新 |
| S-7 | 縮圖區視窗設 `WS_EX_NOACTIVATE` 後點擊行為是否正常 |

### 階段 1：單螢幕可用版
範圍：FR-1、FR-2、FR-3（`maximize` 與 `switch`）、3.1 的 AppBar 縮圖區、固定直排槽位。

驗收：
- 選 4 個視窗後，縮圖區出現 4 個即時縮圖。
- 點任一縮圖，該視窗在操作區最大化且不蓋到縮圖區，4 個縮圖都還在且持續更新。
- 關掉其中一個來源視窗，程式不當掉，該槽位顯示等待中。
- 結束程式後工作區恢復原狀（AppBar 有正確移除）。

### 階段 2：自訂佈局與多螢幕
範圍：FR-4、FR-5、FR-6、FR-7、3.2 的兩種縮圖區型態。

驗收：
- 編輯模式下調整槽位後存檔，重開程式自動還原佈局並自動綁回視窗。
- 雙螢幕下指定焦點螢幕，點縮圖後視窗出現在焦點螢幕。
- 拔掉第二個螢幕後程式自動退回單螢幕模式，不當掉。
- 兩個螢幕縮放比例不同（例如 100% 與 150%）時，縮圖位置與大小正確。

### 階段 3：進階
範圍：`crossDesktop: pull`、`placeOnActivate: fit`、最小化視窗的提示與自動還原選項。

## 9. 已知限制與風險

| 編號 | 內容 | 處理方式 |
|---|---|---|
| R-1 | 最小化的視窗縮圖不會更新，這是 DWM 的限制 | 槽位上顯示提示圖示；程式本身絕不最小化來源視窗 |
| R-2 | 一般權限的程式無法操作系統管理員權限的視窗（UIPI） | 偵測到時在槽位標示，並提示可改用系統管理員身分執行本工具 |
| R-3 | 官方 `MoveWindowToDesktop` 對其他行程的視窗通常會失敗，`pull` 可能需要未公開的 COM 介面，而那些介面會隨 Windows 版本改變 | 放在階段 3，獨立成一個可關閉的模組，失敗時退回 `switch` |
| R-4 | 全螢幕獨佔的程式（部分遊戲、播放器）可能無視 AppBar | 列為不支援 |
| R-5 | 受 DRM 保護的畫面縮圖會是黑的 | 列為不支援 |
| R-6 | MobaXterm 內建的 MobaTextEditor 被本工具最大化（槽位設為最大化後點縮圖）時會先卡住再閃退（2026-11-09 使用者回報） | 列為已知異常，寫入使用手冊；不追蹤、不處理，請用戶避免同時使用 |

## 10. 非目標

- 不做畫面錄製、截圖、串流。
- 不做縮圖內的滑鼠穿透操作（OnTopReplica 的 click-through / click-forwarding）。
- 不支援 Windows 10 以前的系統。
- 不做安裝程式、不做自動更新。

## 11. 建置與發佈

```
dotnet build
dotnet test
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

- 產物是單一 `ThumbDock.exe`。self-contained 的檔案較大（數十 MB 以上），這是免安裝的代價。
- WinForms 不支援 trimming，不要開 `PublishTrimmed`。
- 另外提供一個 framework-dependent 的小檔版本當作選項（需要目標機器已安裝 .NET Desktop Runtime）。

## 12. 給 Claude Code 的工作守則

- 所有 Win32 handle 與 DWM 縮圖都要成對釋放；`ThumbnailHandle` 實作 `IDisposable`。
- 程式結束、當掉、被強制關閉前，都要盡力移除 AppBar（處理 `ApplicationExit` 與未處理例外）。
- 對外部視窗的每個 API 呼叫都要假設視窗可能已經不存在，檢查回傳值，不可丟出未處理例外。
- 座標換算、比對規則、佈局檔讀寫這些純邏輯要有單元測試；需要實機畫面的部分，在每個階段結束時列出手動測試步驟給使用者執行。
- 介面文字用繁體中文（台灣），程式碼與註解用英文。
- 不確定的 Windows 行為先寫小程式實測，不要憑印象下結論。
