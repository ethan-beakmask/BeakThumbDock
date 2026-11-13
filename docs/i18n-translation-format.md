# 多國語言階段 2：翻譯檔格式與翻譯工具規格

狀態：2026-11-09 使用者確認第 7 節五點，**已在 0.11.0 實作**（`Core/LangFormat.cs`、`Core/Loc.cs`、`tools/LangTool/`，測試 `LangToolTests`、`LocalizationTests`）。`docs/i18n.md` 的寫程式規則不變，其「新增一個語言」一節改為指向本文件。

## 1. 背景與決策

- 原文（繁中）當鍵的機制維持不變（`Loc.T("原文")`），不改成編號制。理由見 2026-11-09 討論：編號制解決不了「原文改意思、舊譯文照常顯示」的問題，反而讓它無聲；程式碼可讀性下降；275 處呼叫與測試都要重寫。
- 英文是**樞紐語言**：譯者從英文翻，繁中只作對照。英文由程式碼同步維護（`LocalizationTests` 強制），品質要當正式文案看待。
- 過時偵測靠「每條譯文記下翻譯當時的英文」：英文一變，工具就把該條標成「需覆核」。這是 gettext fuzzy 的概念，用 JSON 實作。
- 流水號只存在匯出的 CSV 裡，供人溝通（「第 37 筆翻錯」），不進程式碼、不進 JSON；匯入時以繁中原文為鍵比對。

## 2. 語言檔格式

### 2.1 英文（`ThumbDock/Lang/en.json`）不變

```json
{
  "code": "en",
  "language": "English",
  "status": "Reference translation (maintained with the code)",
  "strings": { "原文": "English text" }
}
```

扁平格式。英文永遠與程式碼同步，所以不需要「翻譯當時的原文」欄。鍵依序數（ordinal）排序，工具與人工編輯都維持這個順序，方便 diff。

### 2.2 其他語言（格式 2）

```json
{
  "code": "ja",
  "language": "日本語",
  "status": "機械翻訳の初稿、ネイティブ未確認",
  "strings": {
    "設定…": { "text": "設定…", "en": "Settings…" },
    "找不到佈局「{0}」。現有佈局：{1}": {
      "text": "レイアウト「{0}」が見つかりません。既存のレイアウト：{1}",
      "en": "Layout \"{0}\" not found. Existing layouts: {1}",
      "note": "訳者メモ（任意）"
    }
  }
}
```

| 欄位 | 必填 | 說明 |
|---|---|---|
| `code` | 是 | BCP 47 語言代碼（`ja`、`zh-CN`、`de`），與檔名相同 |
| `language` | 是 | 該語言的自稱，顯示在設定視窗下拉 |
| `status` | 否 | 誠實標示品質（機器翻譯、母語者已審閱…），用該語言寫，顯示在下拉的括號內 |
| `strings.<原文>.text` | 是 | 譯文。空字串視同未翻譯 |
| `strings.<原文>.en` | 是 | 翻譯當時看到的英文全文（不用雜湊，為了能在 CSV 顯示「舊英文」給覆核者比對） |
| `strings.<原文>.note` | 否 | 譯者備註，匯出匯入會保留，程式不使用 |

**相容舊格式**：值若是純字串（0.10.x 的 `"原文": "譯文"`），程式照常使用；工具視為「沒有 `en` 基準」，標成「需覆核」，匯入或 `accept` 後補上 `en`。

**鍵的順序**：與 `en.json` 相同（序數排序）。工具寫檔時重排；多餘的鍵（英文已沒有的原文）寫檔時**移除**，移除前在匯出的 CSV 列出供譯者參考（見 4.2）。

### 2.3 檔案位置：隨 exe 出貨的語言改為內嵌

產品是免安裝單檔 exe，不應該靠旁邊的散檔才有日文。因此：

- `ThumbDock/Lang/*.json` 全部內嵌（csproj 改成 `Lang/*.json` 萬用字元，資源名 `ThumbDock.lang.<code>.json`）。`en.json` 以外的檔案一律是格式 2。
- `config/lang/*.json` 保留，給使用者自行新增或修正：同代碼時**逐鍵覆蓋**內嵌版本（可以只放要修的幾條），`language`／`status` 以外掛檔為準；不同代碼就是新增語言。
- 設定視窗的下拉來源＝內嵌語言 ∪ 外掛語言。
- 測試用的 `publish/self-contained/config/lang/ja.json`（英文複本）在真正的 `Lang/ja.json` 進來後刪除，`config/language.txt` 改回 `auto`。

## 3. 執行期（`Core/Loc.cs`）要改的行為

1. 讀取 `strings` 時同時接受純字串與 `{ text, en, note }` 物件，只取 `text`。
2. 列舉內嵌資源 `ThumbDock.lang.*.json` 當作出貨語言；外掛檔逐鍵覆蓋。
3. **載入時驗證佔位符**：譯文裡的每個 `{` 必須是 `{{` 或 `{n}`，且 `n` 不超過英文（即原文）用到的最大編號。不合格的條目丟棄並記 log，退回英文。目前 `string.Format` 遇到 `{2}` 卻只有兩個引數會丟 `FormatException`，手改壞一條語言檔就能讓程式當掉，這是現有漏洞。
4. `Loc.T(source, args)` 再加一層保險：`FormatException` 時改用原文格式化。
5. 載入時的問題（JSON 壞掉、條目被丟棄）收集成 `Loc.Warnings`，設定視窗的「語言」區塊顯示一行灰字警告（回應 backlog「語言檔壞掉時在設定視窗顯示警告」）。
6. 「需覆核」的條目**照常顯示**：英文小改通常只是潤飾，顯示稍舊的譯文比退回英文好。覆核是工具與維護者的事，不是執行期的事。

## 4. 翻譯工具 `LangTool`

### 4.1 形式

- 獨立主控台專案 `tools/LangTool/LangTool.csproj`（`net10.0`，不用 WinForms，不出貨），加進 `ThumbDock.slnx`，核心邏輯（讀寫語言檔、狀態計算、CSV）放在可測試的類別，單元測試放 `ThumbDock.Tests`（新增 `LangToolTests`）。
- 執行：`& $dn run --project tools/LangTool -- <命令> …`。只有維護者用，譯者只碰 CSV。
- 預設路徑：`--lang-dir` 預設 `ThumbDock/Lang`（相對於目前目錄往上找到 `ThumbDock.slnx`）；目標檔＝`<lang-dir>/<code>.json`，可用 `--file` 指定（例如指到 `config/lang/`）。
- 工具的訊息用繁中（給維護者），CSV 的欄名與狀態值用英文（給譯者）。

### 4.2 每條字串的狀態（工具計算，不存檔）

| 狀態（CSV 值） | 條件 | 匯入時 |
|---|---|---|
| `untranslated` | 英文有、目標檔沒有，或 `text` 為空 | 譯文非空則寫入 |
| `review` | `text` 非空，但 `en` ≠ 目前英文，或沒有 `en`（舊格式） | 同上 |
| `translated` | `text` 非空且 `en` ＝ 目前英文 | 同上 |
| `stale` | 目標檔有、英文沒有（原文已刪或改寫） | 不寫入；匯出時列在最後供參考 |

### 4.3 命令

```
status  [<code>]                 每個語言（或指定語言）的 translated / review / untranslated / stale 數量
new     <code> --name <自稱> [--status <文字>]
                                 建立空的格式 2 檔案（已存在則拒絕）
export  <code> [--out <csv>] [--pending]
                                 匯出 CSV；--pending 只匯 untranslated、review、stale
import  <code> <csv>             依 CSV 更新目標檔（見 4.5）
accept  <code> [--key <原文>]    把 review 條目的 en 蓋成目前英文（維護者已在程式裡確認過時用）；
                                 不給 --key 就是全部
check   [--dir <資料夾>]         驗證語言檔：JSON 可解析、欄位齊全、佔位符合法、沒有 stale 鍵；
                                 預設驗 ThumbDock/Lang，--dir 可驗 publish/.../config/lang。有錯誤回傳非零
```

### 4.4 CSV 格式（匯出）

- 編碼 **UTF-8 加 BOM**（Excel 在繁中 Windows 直接點開不會變亂碼），分隔逗號，欄位一律加雙引號，換行保留在儲存格內（Excel 以 Alt+Enter 顯示）。
- 欄位與順序：

| 欄 | 內容 | 譯者可改 |
|---|---|---|
| `No` | 流水號，從 1 起，僅本次匯出有效 | 否 |
| `Status` | 4.2 的狀態 | 否 |
| `English` | 目前英文（譯者的來源） | 否 |
| `Translation` | 既有譯文；未翻譯則空白 | **是** |
| `Chinese (source)` | 繁中原文，也是比對鍵 | 否 |
| `Previous English` | `review` 時填翻譯當時的英文，其餘空白 | 否 |
| `Note` | 既有備註 | 是 |

- 檔名預設 `ThumbDock-<code>-<yyyyMMdd>.csv`，寫在目前目錄。
- `stale` 列排在最後、`Translation` 填舊譯文、`English` 留空，純供譯者複製到新原文用。
- Excel 會把以 `=`、`+`、`-`、`@` 開頭的儲存格當公式（目前有一條英文是 `= number of slots`）。匯出時這類儲存格前面加一個 `'`，匯入時去掉開頭的單一 `'`。

### 4.5 匯入規則

1. 以 `Chinese (source)` 欄為鍵比對；鍵不在目前英文裡的列忽略並回報（可能是 stale 列或譯者改到了鍵）。
2. `Translation` 非空的列：寫入 `text`，`en` 蓋成**目前**英文（不是 `Previous English`），`note` 照寫。譯者看過這份 CSV 的英文而留下的譯文，就視為已確認。
3. `Translation` 空白的列：目標檔的既有條目不動（避免譯者沒填到就把舊譯文清掉）。
4. **佔位符檢查**：譯文的 `{n}` 集合必須是英文 `{n}` 集合的子集（允許少用，不允許多出或超出編號），大括號必須成對。不合格的列**不寫入**，逐列回報。
5. **前後空白**（`Catalog.MatchOuterWhitespace`，0.11.1 修訂）：每條譯文先去掉自己的前後空白，再套用英文的前導與尾隨空白，避免 Excel 吃掉空格（選單縮排的 9 條、結尾帶空白或換行的串接片段與長文）。例外是全形字元（U+3000–U+30FF、U+4E00–U+9FFF、U+FF00–U+FFEF）旁邊不補半形空白：
   - 英文的前導空白只有單一空白、且譯文第一個字元是全形字元時不加（`" (primary)"` → `"（メイン）"`）；兩個以上的前導空白（選單縮排）照加。
   - 譯文最後一個字元是全形字元時不加尾隨空白（`", "` → `"、"`），否則照英文（`", "` → 德文 `", "`、`"[Minimized] "` → `"[最小化] "`）。
   - 尾隨的換行（`\r\n`、`\n`）一律照英文補回。
6. 寫檔前備份為 `<code>.json.bak`；寫檔後印出摘要（新增／更新／拒絕／忽略數量）並建議執行 `check`。

### 4.6 流程範例（新增日文）

```powershell
$lt = { & $dn run --project tools/LangTool -- @args }
& $lt new ja --name 日本語 --status "機械翻訳の初稿"
& $lt export ja                      # ThumbDock-ja-20261110.csv，全部 untranslated
# 交給譯者，或先用機器翻譯填 Translation 欄
& $lt import ja ThumbDock-ja-20261110.csv   # 審閱後的 CSV 收在 docs/translations/
& $lt check
& $dn build ThumbDock.slnx           # ja.json 內嵌進 exe
# 之後原文改了：
& $lt status ja                      # 看到 review: 3
& $lt export ja --pending            # 只匯 3 條，Previous English 欄顯示改了什麼
```

## 5. 測試

- `LocalizationTests` 既有三條檢查不變（原文 ↔ 英文）。
- 新增：每個內嵌語言檔都能解析、沒有 stale 鍵、佔位符合法（`check` 的邏輯直接當測試跑）。
- `LangToolTests`：狀態計算（四種狀態各一例、舊格式視為 review）、CSV 來回（含多行、前導空白、`'` 前綴、佔位符拒絕）、匯入不清空既有譯文、`accept`。
- `Loc` 新增：物件格式讀取、外掛逐鍵覆蓋、壞佔位符被丟棄且 `Warnings` 有記錄、`FormatException` 保險。

## 6. 不在本階段

- 日文、簡中的實際譯文與母語者審閱（階段 2 的下一步，用本工具產 CSV）。
- 版面依語言調整（階段 3）。
- README、Chrome 提醒說明的英文版（階段 5）。
- 同一原文在不同位置需要不同譯法：目前沒有案例，出現時改寫原文或在原文加上下文後綴即可，不另設機制。

## 7. 使用者已確認的決策（2026-11-09）

1. 英文當樞紐語言、CSV 欄名與狀態值用英文。
2. 出貨語言內嵌進 exe，`config/lang` 改為覆蓋與新增用。
3. `review` 條目在程式裡照常顯示（不退回英文）。
4. 工具用 C# 主控台專案而非 Python 腳本。
5. CSV 交付而非 XLSX（避免工具多一個相依套件；譯者用 Excel 開 CSV 即可）。

## 8. 實作備註

- `LangTool` 是 `net10.0-windows` 主控台專案，以 `ProjectReference` 共用 `ThumbDock/Core/LangFormat.cs`（解析與佔位符規則只有一份）。直接執行 `tools/LangTool/bin/.../LangTool.exe` 前要設 `$env:DOTNET_ROOT`（見 `docs/dotnet-sdk-portable.md`）；用 `dotnet run` 則不用。
- 原文沒有佔位符的字串從不經過 `string.Format`，所以其譯文裡的大括號不檢查。
- `import` 把「譯文非空的列」一律視為譯者已看過目前英文而確認，`en` 蓋成目前英文；`--pending` 匯出就是為了縮小譯者要看的範圍。
- `Save` 一律重寫整個檔案（英文鍵序、移除多餘鍵、`.bak` 備份），未翻譯的鍵不寫入。
- 測試專案關閉平行執行（`AssemblyInfo.cs`），因為 `Loc` 是行程共用狀態，`LocalizationTests` 會切換語言。
