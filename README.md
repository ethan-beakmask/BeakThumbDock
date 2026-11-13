# ThumbDock

Windows 的免安裝小工具：把多個視窗（包含其他虛擬桌面上的視窗）變成即時縮圖，排在螢幕邊緣的縮圖區。點一下縮圖就把該視窗帶到前面，縮圖常駐，適合同時監看多個視窗或在大量視窗之間快速切換。

- 縮圖區可靠左／右／上／下，或蓋滿整個螢幕（縮圖牆）
- 單螢幕、雙螢幕、多個虛擬桌面
- 依規則自動找回視窗，重新開機後不必重新設定
- 多個佈局、全域快速鍵、啟動時自動開啟程式
- 介面語言：繁體中文、English、日本語、简体中文

## 使用

到 Releases 下載 `ThumbDock.exe`，放進自己有寫入權限的資料夾後執行。不需要安裝，也不需要系統管理員權限。

操作說明見 [docs/manual.md](docs/manual.md)（網頁版：[docs/manual.html](docs/manual.html)）。

在公司環境被端點防護軟體攔截時，請見手冊第 16 節。

## 翻譯

日文與簡體中文是機器翻譯的初稿，沒有經過母語者審閱，歡迎回報或提交修正。

- 新增語言或修正譯文的方法：[docs/i18n.md](docs/i18n.md)、[docs/i18n-translation-format.md](docs/i18n-translation-format.md)
- 審閱用的 CSV：`docs/translations/`
- 語言檔可以直接放進 exe 旁的 `config\lang\` 使用，不需要重新建置

## 從原始碼建置

需要 .NET 10 SDK（免安裝版的做法見 [docs/dotnet-sdk-portable.md](docs/dotnet-sdk-portable.md)）。

```powershell
dotnet build ThumbDock.slnx
dotnet test ThumbDock.slnx
dotnet publish ThumbDock\ThumbDock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish\self-contained
```

手冊的網頁版由 `python tools/build-manual.py` 從 `docs/manual.md` 產生。

## 專案結構

| 路徑 | 內容 |
|---|---|
| `ThumbDock/` | 主程式（`Native/`、`Core/`、`Layout/`、`UI/`、`Lang/`）與單元測試 |
| `tools/LangTool/` | 翻譯檔維護工具 |
| `spike/` | 早期的技術驗證程式，不是正式程式 |
| `docs/` | 手冊與格式說明 |
| `THUMBDOCK_SPEC.md` | 原始需求規格 |

## 作者

Ethan Yu（大E）
