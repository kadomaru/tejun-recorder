# 手順レコーダー (tejun-recorder)

Windows 向けのステップ記録ツールです。操作をクリックのたびに画面ごと記録し、クリックした場所に印を付けて、そのまま手順書として保存できます。

- **画面をそのまま記録** … クリックした瞬間の画面を、メニューやツールチップ、マウスカーソルも含めて劣化なし（PNG）で撮ります。拡大表示（125%・150% など）の画面でも、実際の画素どおりに撮ります
- **クリックした場所を自動で表示** … 赤い丸の印を付けます。右クリックとダブルクリックは見分けて表示します
- **説明文を自動で下書き** … 「『保存』ボタンをクリックします」のように、押した部品の名前から説明を作ります。あとから自由に書き直せます
- **PNG / PDF / HTML で保存** … PDF は文字を検索・コピーでき、HTML は画像を埋め込んだ 1 ファイルです
- **インストール不要** … exe ファイル 1 つで動きます。記録した内容が外部へ送られることはありません

## 使い方

1. [Releases](https://github.com/kadomaru/tejun-recorder/releases) から `TejunRecorder.exe` をダウンロードして開きます
2. **「記録をはじめる」** を押します。窓が隠れ、画面の下に小さな操作バーが出ます
3. いつもどおり操作します。クリックするたびに手順が 1 つ増えます
4. 操作バーの **「記録を終了」** を押します
5. 説明を手直しし、順番の入れ替えや削除をして、**PDF・HTML・PNG 画像** のどれかで保存します

操作バーには「一時停止」と「今の画面を撮る」（クリックなしで撮影）もあります。操作バー自体は記録に写りません（Windows 10 バージョン 2004 以降）。

撮る範囲は、はじめの画面で選べます。

| 撮る範囲 | 内容 |
| --- | --- |
| クリックした画面の全体（おすすめ） | クリックしたモニターを丸ごと撮ります |
| クリックしたウィンドウだけ | クリックしたウィンドウの範囲だけを撮ります |
| つないでいる画面をすべて | 複数のモニターを 1 枚にまとめて撮ります |

### 記録されるもの・されないもの

- 記録するのは、マウスのクリックと、その瞬間の画面です
- キーボードで打った文字は記録しません（画面に表示されている文字は、画像として写ります）
- パスワードなど見せたくないものが画面に出ているときは、「一時停止」を使うか、あとでその手順を削除してください

## 動作環境

- Windows 10 / 11（64 ビット）

管理者として動いているアプリ（タスク マネージャーなど）の上でのクリックは、Windows の仕組み上、通常の権限では検知できません。その場合は手順レコーダーも管理者として実行してください。

## 開発

.NET 10 SDK が必要です。画面は [Avalonia](https://avaloniaui.net/)、画像と PDF の生成は [SkiaSharp](https://github.com/mono/SkiaSharp) を使っています。

```
dotnet test tests/Tejun.Tests                                  # テスト
dotnet run --project src/Tejun.App                             # 起動
dotnet publish src/Tejun.App -c Release -r win-x64 -o publish  # 配布用の exe を作る
```

| 場所 | 内容 |
| --- | --- |
| `src/Tejun.Core` | 手順のデータ、印の描画、PNG / PDF / HTML の書き出し |
| `src/Tejun.App` | 画面と、Windows でのクリック検知・画面撮影（`Platform/Windows`） |
| `tests/Tejun.Tests` | 書き出しのテストと、画面を表示せずに操作の流れを通すテスト |

### Microsoft Store 用のパッケージ

Store から入れたアプリは Microsoft が署名するため、ブラウザや Windows の「信頼できない」という警告が出ません。提出するパッケージ（MSIX）は GitHub Actions が毎回作り、`TejunRecorder-store-msix` という成果物に入れます。

1. [storedeveloper.microsoft.com](https://storedeveloper.microsoft.com/) で個人の開発者として登録し、パートナー センターでアプリの名前を予約します
2. パートナー センターの「製品 ID」に出る 3 つの値を、GitHub の Settings → Secrets and variables → Actions → Variables に登録します

   | Variable | パートナー センターでの名前 |
   | --- | --- |
   | `STORE_IDENTITY_NAME` | パッケージ/ID/名前 |
   | `STORE_PUBLISHER` | パッケージ/ID/発行元（`CN=…`） |
   | `STORE_PUBLISHER_DISPLAY_NAME` | パッケージ/プロパティ/発行元表示名 |

3. GitHub Actions を動かし直して、成果物の `.msix` をパートナー センターの申請にアップロードします。`runFullTrust` の用途を聞かれたら、「クリックの検知と画面の撮影を行うデスクトップアプリのため」と答えます

手元で作るときは、Windows SDK を入れた Windows で `pwsh packaging/msix/build.ps1` を実行します。Store は 0 で始まるバージョンを受け付けないので、バージョン（`Directory.Build.props`）は 1.0.0 以上にします。

Windows 以外でも起動でき、その場合は見本の手順で画面の動きを確かめるモードになります。Windows 上での記録そのものは、GitHub Actions が実際にクリックを起こして確かめています（`TejunRecorder.exe --selftest 出力先`）。

## ライセンス

[MIT License](LICENSE)
