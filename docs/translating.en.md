# Translating BeakThumbDock

How to fix a translation or add a new interface language. Other languages: [日本語](translating.ja.md). The design notes behind this (Traditional Chinese) are in [i18n.md](i18n.md) and [i18n-translation-format.md](i18n-translation-format.md).

## How it works

- The source text in the code is **Traditional Chinese (Taiwan)**, and the source text itself is the lookup key: `Loc.T("設定…")`.
- **English is the pivot language.** Translators translate from English; the Chinese text is only there for reference. English lives in `ThumbDock/Lang/en.json` and is kept in step with the code by a unit test.
- Every `ThumbDock/Lang/*.json` file is embedded in the exe. Files in `config\lang\` next to the exe add a language or override single entries of an embedded one.
- A missing entry falls back to English, then to the Chinese source text.
- Japanese and Simplified Chinese are machine-translated first drafts. Right-to-left languages are not supported.

## Option A: fix a few strings without building

Create `config\lang\<code>.json` next to `ThumbDock.exe` (for example `config\lang\ja.json`) and restart ThumbDock. A file with the same code as a built-in language overrides it **entry by entry**, so it only needs the entries you want to change. A new code becomes a new language in Settings > Language.

```json
{
  "code": "ja",
  "language": "日本語",
  "status": "ネイティブ確認済み",
  "strings": {
    "設定…": { "text": "設定…", "en": "Settings…" }
  }
}
```

| Field | Required | Meaning |
|---|---|---|
| `code` | yes | BCP 47 language code (`ja`, `zh-CN`, `de`), same as the file name |
| `language` | yes | The language's own name, shown in the drop-down |
| `status` | no | An honest quality label (machine translated, reviewed by a native speaker…), written in that language |
| `strings.<source>.text` | yes | The translation. An empty string counts as untranslated |
| `strings.<source>.en` | yes | The English text you translated from; used to detect outdated entries |
| `strings.<source>.note` | no | Translator's note; kept by the tool, ignored by the program |

The key is the Chinese source text. The easiest way to get the keys is to copy entries from the review CSV in [translations/](translations/), where each row has the English text, the current translation and the Chinese key.

## Option B: review the whole language in a spreadsheet

The files in [translations/](translations/) (`ThumbDock-<code>-<date>.csv`) open directly in Excel (UTF-8 with BOM).

| Column | Content | Edit it? |
|---|---|---|
| `No` | Row number, valid for this export only | no |
| `Status` | `untranslated`, `review`, `translated` or `stale` | no |
| `English` | Current English text (translate from this) | no |
| `Translation` | Existing translation, or empty | **yes** |
| `Chinese (source)` | Chinese source text; also the matching key | no |
| `Previous English` | For `review` rows: the English text at the time of the last translation | no |
| `Note` | Existing note | yes |

Fill in or correct the `Translation` column, leave the other columns as they are, and send the CSV back through an issue or pull request. Rows left empty keep their existing translation.

Status values:

| Status | Meaning |
|---|---|
| `untranslated` | No translation yet |
| `review` | Translated, but the English text has changed since then |
| `translated` | Translated and up to date |
| `stale` | The source text no longer exists; listed last for reference only, not imported |

## Rules for translations

- Keep placeholders such as `{0}` and `{1}` exactly as they are. You may change their order. A placeholder that is not in the English text, or unbalanced braces, makes the row rejected on import and the entry discarded at run time.
- Leading and trailing spaces and line breaks are taken from the English text automatically; you do not need to preserve them by hand.
- Cells that begin with `=`, `+`, `-` or `@` are exported with a leading `'` so Excel does not treat them as formulas. Leave that apostrophe alone.
- Keep the names of things consistent within one language (slot, layout, thumbnail area, launch config).

## For maintainers: LangTool

`tools/LangTool` maintains the language files. Do not write the `{ text, en, note }` JSON by hand in the repository; use the tool so key order and backups stay consistent.

```powershell
dotnet run --project tools/LangTool -- status              # counts per language
dotnet run --project tools/LangTool -- new de --name Deutsch --status "Maschinelle Übersetzung, ungeprüft"
dotnet run --project tools/LangTool -- export de           # writes ThumbDock-de-<date>.csv
dotnet run --project tools/LangTool -- export de --pending # only untranslated, review and stale rows
dotnet run --project tools/LangTool -- import de ThumbDock-de-<date>.csv
dotnet run --project tools/LangTool -- accept de           # mark review entries as still correct
dotnet run --project tools/LangTool -- check               # validate every language file; non-zero exit on error
```

- `import` matches rows by the Chinese source text, writes non-empty translations, records the current English text with each one, and saves a `<code>.json.bak` backup first.
- `--file` points the tool at another file, for example one in `config\lang\`; `check --dir <folder>` validates another folder.
- After `import`, run `check`, then `dotnet build ThumbDock.slnx`; the new file in `ThumbDock/Lang/` is embedded and appears in the Language drop-down.
- When source text in the code changes, `en.json` must be updated in the same change (the `LocalizationTests` unit tests enforce this). Then run `status` to see which entries of the other languages need review.
