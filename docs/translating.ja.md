# BeakThumbDock の翻訳

訳文の修正や、新しい表示言語の追加方法を説明します。他の言語：[English](translating.en.md)。設計の経緯（繁体字中国語）は [i18n.md](i18n.md) と [i18n-translation-format.md](i18n-translation-format.md) にあります。

この文書は機械翻訳をもとにした初稿で、ネイティブによる確認を受けていません。

## 仕組み

- コード内の原文は**繁体字中国語（台湾）**で、原文そのものが検索キーです：`Loc.T("設定…")`。
- **英語が基準言語（ピボット言語）です。** 翻訳者は英語から翻訳し、中国語は参考用です。英語は `ThumbDock/Lang/en.json` にあり、単体テストによってコードと同期が保たれています。
- `ThumbDock/Lang/*.json` はすべて exe に埋め込まれます。exe の隣の `config\lang\` に置いたファイルは、言語を追加するか、埋め込み言語の項目を個別に上書きします。
- 項目がない場合は英語に、さらになければ中国語の原文にフォールバックします。
- 日本語と簡体字中国語は機械翻訳の初稿です。右から左に書く言語には対応していません。

## 方法 A：ビルドせずに数か所だけ直す

`ThumbDock.exe` の隣に `config\lang\<コード>.json`（例：`config\lang\ja.json`）を作り、ThumbDock を再起動します。組み込み言語と同じコードのファイルは**項目単位で**上書きするため、変更したい項目だけを書けば十分です。新しいコードは「設定」>「言語」に新しい言語として現れます。

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

| フィールド | 必須 | 説明 |
|---|---|---|
| `code` | はい | BCP 47 言語コード（`ja`、`zh-CN`、`de`）。ファイル名と同じにします |
| `language` | はい | その言語での言語名。ドロップダウンに表示されます |
| `status` | いいえ | 品質の正直な表示（機械翻訳、ネイティブ確認済みなど）。その言語で書きます |
| `strings.<原文>.text` | はい | 訳文。空文字列は未翻訳として扱われます |
| `strings.<原文>.en` | はい | 翻訳時に見た英語の全文。古くなった項目の検出に使います |
| `strings.<原文>.note` | いいえ | 翻訳者のメモ。ツールは保持し、プログラムは使いません |

キーは中国語の原文です。キーを得るには、[translations/](translations/) にあるレビュー用 CSV から項目をコピーするのが簡単です。各行に英語、現在の訳文、中国語のキーが入っています。

## 方法 B：表計算ソフトで言語全体を見直す

[translations/](translations/) のファイル（`ThumbDock-<コード>-<日付>.csv`）は Excel でそのまま開けます（BOM 付き UTF-8）。

| 列 | 内容 | 編集 |
|---|---|---|
| `No` | 連番。そのエクスポートでのみ有効 | 不可 |
| `Status` | `untranslated`、`review`、`translated`、`stale` のいずれか | 不可 |
| `English` | 現在の英語（ここから翻訳します） | 不可 |
| `Translation` | 既存の訳文。なければ空欄 | **可** |
| `Chinese (source)` | 中国語の原文。照合キーでもあります | 不可 |
| `Previous English` | `review` の行のみ：前回翻訳した時点の英語 | 不可 |
| `Note` | 既存のメモ | 可 |

`Translation` 列を記入または修正し、他の列はそのままにして、issue か pull request で CSV を送ってください。空欄のままの行は既存の訳文が保たれます。

状態の値：

| 状態 | 意味 |
|---|---|
| `untranslated` | まだ訳文がない |
| `review` | 訳文はあるが、その後に英語が変更された |
| `translated` | 訳文があり、最新 |
| `stale` | 原文がすでに存在しない。参考用に最後に並び、インポートされません |

## 訳文のルール

- `{0}` や `{1}` などのプレースホルダーはそのまま残してください。順序は変えてかまいません。英語にないプレースホルダーや、対になっていない波かっこがあると、その行はインポート時に拒否され、実行時にもその項目は破棄されます。
- 前後の空白と改行は英語から自動的に引き継がれるため、手で保つ必要はありません。全角文字の隣には半角スペースは追加されません。
- `=`、`+`、`-`、`@` で始まるセルは、Excel が数式として扱わないよう、先頭に `'` を付けてエクスポートされます。このアポストロフィはそのままにしてください。
- 用語は 1 つの言語の中で統一してください（スロット、レイアウト、サムネイル領域、起動設定ファイル）。

## メンテナー向け：LangTool

言語ファイルは `tools/LangTool` で管理します。リポジトリ内の `{ text, en, note }` 形式の JSON を手で書かず、キーの順序とバックアップをそろえるためにツールを使ってください。

```powershell
dotnet run --project tools/LangTool -- status              # 言語ごとの件数
dotnet run --project tools/LangTool -- new de --name Deutsch --status "Maschinelle Übersetzung, ungeprüft"
dotnet run --project tools/LangTool -- export de           # ThumbDock-de-<日付>.csv を出力
dotnet run --project tools/LangTool -- export de --pending # untranslated、review、stale の行のみ
dotnet run --project tools/LangTool -- import de ThumbDock-de-<日付>.csv
dotnet run --project tools/LangTool -- accept de           # review の項目を「このままで正しい」として確定
dotnet run --project tools/LangTool -- check               # すべての言語ファイルを検証。エラーがあれば 0 以外で終了
```

- `import` は中国語の原文で行を照合し、空でない訳文を書き込み、その時点の英語を一緒に記録します。書き込み前に `<コード>.json.bak` というバックアップを作ります。
- `--file` で別のファイル（例：`config\lang\` 内のファイル）を対象にできます。`check --dir <フォルダー>` で別のフォルダーを検証できます。
- `import` の後は `check` を実行し、続けて `dotnet build ThumbDock.slnx` を実行します。`ThumbDock/Lang/` の新しいファイルが埋め込まれ、「言語」のドロップダウンに現れます。
- コード内の原文を変更したときは、同じ変更の中で `en.json` も更新する必要があります（単体テスト `LocalizationTests` が検査します）。その後 `status` を実行すると、他の言語で見直しが必要な項目がわかります。
