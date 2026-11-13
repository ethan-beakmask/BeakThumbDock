# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 專案概覽

ThumbDock：Windows 免安裝工具（C#、WinForms、.NET 10）。用 DWM Thumbnail API 把多個來源視窗（含其他虛擬桌面）以即時縮圖排在 AppBar 縮圖區，點縮圖把視窗帶到操作區，縮圖常駐。需求以 `THUMBDOCK_SPEC.md` 為準；驗收狀態與「與規格的已同意差異」見 `docs/internal/acceptance.md`（規格階段 0～3 已全部驗收，**實作與規格有出入時以 acceptance.md 為準**，例如 `place` 是每槽位而非整個佈局）。下一版待辦見 `docs/internal/next-version-backlog.md`。

經 Bash 工具的 python heredoc 寫入含 `\n` 的 C# 字串時會被先解跳脫成真的換行，請用 `chr(92)` 或直接用 Edit 工具。

`docs/internal/`（驗收紀錄、待辦、手動測試步驟、spike 結果）是開發過程文件，只留在本機，已被 `.gitignore` 排除，公開 repo 裡沒有；`publish/`、`backup-source/` 也不進 repo。翻譯審閱用 CSV 在 `docs/translations/`。

文件、介面文字用繁體中文（台灣）；程式碼與註解用英文。

## 建置與測試

此機器 PATH 沒有 dotnet，SDK 是免安裝版（2026-11-07 已裝在 `%LOCALAPPDATA%\Microsoft\dotnet`，10.0.403）（見 `docs/dotnet-sdk-portable.md`），要用完整路徑：

```powershell
$dn = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dn build ThumbDock.slnx
& $dn test ThumbDock.slnx
& $dn test ThumbDock.slnx --filter "FullyQualifiedName~SlotGeometryTests"   # 單一測試類別／方法
```

發佈（產物放 `publish/`，framework-dependent 版執行前需設 `$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"`）：

```powershell
& $dn publish ThumbDock\ThumbDock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish\self-contained
```

- WinForms 不可開 `PublishTrimmed`。
- 目前沒有 lint 設定。
- `spike/ThumbDock.Spike` 是階段 0 的技術驗證程式（有 `--selftest`），不在 `.slnx` 內，也不是正式程式。

## 架構

`ThumbDock/` 依職責分層，分層邊界是理解程式的關鍵：

- `Native/`：全部 P/Invoke 與 COM（`Dwm`、`User32`、`AppBarNative`、`VirtualDesktop`）。簽章由 `NativeMethods.txt` 經 CsWin32 產生，新增 API 要先加進該檔。`VirtualDesktopPull.cs` 用未公開 COM 介面搬移視窗，依 Windows 版本比對介面 ID，不認得的版本會停用並退回 `switch`；Windows 大改版後要在這裡補新 ID。
- `Core/`：與視窗相關的邏輯。純邏輯（`WindowMatcher`、`WindowFilter`、`PlacementPlanner`、`HotkeyParser`、`SlotBinder`）刻意不碰 Win32，才能單元測試；`WindowEnumerator`、`WindowActivator`、`ThumbnailHandle`（`IDisposable`，DWM 縮圖需成對釋放）、`ForegroundTracker`（`SetWinEventHook`）則是實機行為。
- `Layout/`：佈局檔模型與存取。`LayoutStore` 讀寫 exe 旁的 `config/layouts/*.json` 與 `config/settings.json`，I/O 或壞 JSON 只記 log、視為找不到，不丟例外。`SlotGeometry` 負責槽位相對座標（0.0–1.0）↔ 像素。
- `UI/`：`ThumbDockApp`（ApplicationContext，管 tray、快速鍵、存檔 debounce）→ `DockSession`（執行一個佈局：擁有 `DockForm`、自動綁定計時器、啟用視窗、編輯模式、顯示變更）→ `DockForm`／`SlotControl`（縮圖區視窗與槽位繪製、滑鼠）。切換佈局或改縮圖區設定時 `DockForm` 會重建，相依物件須監聽 `DockRebuilt` 重新掛接。

`Program.cs` 解析命令列（`Core/CliOptions`，`--layout`／`--monitor`／`--help`），由 `UI/StartupResolver` 決定佈局與螢幕，並在 `ApplicationExit`／未處理例外／`ProcessExit` 呼叫 `EmergencyCleanup` 移除 AppBar。

多開規則（`Core/InstanceGuard`，具名 Mutex）：每個實體螢幕一個實例、每個佈局名稱一個實例；雙螢幕佈局佔用全部螢幕。最先啟動的實例（Main）擁有全域快速鍵與 `config/settings.json`，其他實例用 `settings.<DISPLAYn>.json`。`AppLog` 為所有實例共用，行首帶 `[DISPLAYn]`。槽位數量增減的純邏輯在 `Layout/SlotCountPlanner`。

單螢幕 fill「縮圖牆」（0.4.0，規格 3.1.1）：`ZoneType.Fill` 且 `ZoneResolution.IsDual == false`（`DockForm.IsWall`）。牆不是 AppBar、不是常駐 TOPMOST（`RaiseWall` 先 TOPMOST 再取消），點縮圖走 `UI/WallController`（記錄視窗原狀態、`ActivationContext.ForceForeground`、回牆時依 `Core/WallPlanner` 還原，順序必須是先還原再拉牆），快速鍵由 `DockSession.ToggleByHotkey` 分流。雙螢幕佈局只剩一個螢幕時仍退回 `Dock`。手動測試見 `docs/internal/single-fill-manual-test.md`。

啟動時呼叫程式（0.3.0，格式見 `docs/launch-config.md`）：`config/launch/<佈局>.json` 由 `LayoutStore.LoadLaunch` 讀取（允許註解與尾逗號），`UI/LaunchRunner` 逐項啟動並以「啟動前後的新視窗差異」綁槽位（不追蹤 PID，因為 Chrome 設定檔、Windows Terminal 會由別的行程開視窗）。由 `AppSettings.AllowLaunch`（預設關）與 `--no-launch` 控制，選單在選擇視窗的「啟動」（`ThumbDockApp.FillLaunchMenu`）；系統匣選單只剩「設定…」「結束」，其餘功能表由 `ThumbDockApp.BuildPickerMenus` 提供給 `WindowPickerForm`。

n×m 格狀排列（0.5.0）：`Layout/GridPlanner.cs`（`GridSpec` 存在佈局檔 `grid`；純計算，單元測試 `GridPlannerTests`）。dock 與 fill 都用，槽位數＝欄×列，依槽位清單順序排；只在設定改變時重排，不覆蓋手動微調。縮圖可在非編輯模式拖曳互換（`DockForm.SwapSlots`，清單順序一起換）。dock 厚度上限為該方向螢幕的 70%（`SlotGeometry.DockThickness`）。

安全啟動（0.7.0）：`--safe` 用內建「預設」佈局（`DefaultLayouts.Enforce`：單螢幕、靠左、200px、8 槽位一欄，載入時強制，不可改／改名／刪除／編輯位置），隱含 `--no-launch`，且不改寫「上次佈局」。左緣滑鼠回牆（0.8.0，預設關）：`Core/HotEdgePlanner.cs`（純邏輯）＋`UI/HotEdgeWatcher.cs`（計時器輪詢）。高權限視窗：`Native/ProcessElevation.cs`，被 UIPI 擋下移動時 `WindowActivator.MoveBlocked` 通知使用者。

多國語言（0.10.0 機制、0.11.0 格式 2 與工具）：使用者看得到的文字一律 `Loc.T("繁中原文")`，原文即鍵，規則見 `docs/i18n.md`（一個 `Loc.T` 一個字串常值、`{0}` 佔位、改字串必須同步 `ThumbDock/Lang/en.json`，`LocalizationTests` 會檢查）。英文是樞紐語言；其他語言是格式 2（`{ text, en, note }`，見 `docs/i18n-translation-format.md`），`Lang/*.json` 全部內嵌進 exe，`config/lang/` 供使用者覆蓋。翻譯檔只用 `tools/LangTool`（`dotnet run --project tools/LangTool -- status|new|export|import|accept|check`）維護，不要手寫格式 2 JSON；`Core/LangFormat.cs` 是 app 與工具共用的唯一解析器。

交接與待辦：先讀 `docs/internal/next-version-backlog.md`；驗收記錄在 `docs/internal/acceptance.md`；各項手動測試在 `docs/internal/*-manual-test.md`（`grid`、`safe-mode`、`hot-edge`、`single-fill`）。

## 實作時必須記得的行為約束

- 槽位以**比對規則**（行程名 + 視窗類別 + 標題 regex）綁定來源視窗，不存 HWND；找不到時顯示「等待中」，由 `DockSession` 的計時器定期自動綁回。
- 對外部視窗的每個 Win32 呼叫都假設視窗可能已消失，檢查回傳值，不可丟未處理例外。
- 絕不最小化來源視窗（最小化後 DWM 縮圖停止更新）。
- 縮圖區視窗需要 `WS_EX_NOACTIVATE` **加上** `WM_MOUSEACTIVATE` 回傳 `MA_NOACTIVATE`，只設前者第一次點擊仍會被啟用（spike S-7）。
- `sizePx` 是 100% 縮放下的像素，實際寬度依螢幕縮放比例放大；manifest 為 PerMonitorV2。
- 其他虛擬桌面的視窗會被標記 `DWMWA_CLOAKED`，不可據此過濾；用 `IVirtualDesktopManager` 判斷。
- 雙螢幕時，只有原本在縮圖螢幕上的視窗，點縮圖才會移到焦點螢幕。
- 要把已最大化的視窗移到另一個螢幕，`SetWindowPlacement` 必須先設一般狀態再最大化（`User32.SetPlacement` 已處理）；其他虛擬桌面的視窗要用 `ForceForeground` 才會切換桌面（`WindowActivator` 已處理）。
- Chrome 在其他桌面／被遮住時縮圖要即時，只有**第一個啟動的 Chrome 行程**需要帶 `--disable-features=CalculateNativeWinOcclusion` 或 `--disable-backgrounding-occluded-windows`（見 `docs/launch-config.md`）。

## 環境注意

- 早期在另一台開發機（Win11 22631）上 AppBar 不會扣除工作區，該問題已排除（使用者 2026-11-09 確認）；目前開發機（Win11 26300）AppBar 正常，AppBar 與縮圖可直接在這台實機驗收，不再需要第二台主機。`fit` 模式自行扣除縮圖區，不受影響。歷史紀錄見 `docs/internal/spike-results.md`。
- 需要實機畫面的功能，每個階段的手動測試步驟在 `docs/internal/stage*-manual-test.md`。
