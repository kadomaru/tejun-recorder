# Microsoft Store に提出する MSIX パッケージを作る（Windows 上で実行する）。
#
#   pwsh packaging/msix/build.ps1 -IdentityName '12345Kadomaru.TejunRecorder' -Publisher 'CN=XXXXXXXX-...' -PublisherDisplayName 'kadomaru'
#
# IdentityName / Publisher / PublisherDisplayName は、パートナー センターの
# 「製品 ID」のページに表示される値をそのまま渡す。省略すると仮の値で作る
# （パッケージが作れることの確認用で、Store には提出できない）。
# 署名はしない。Store が提出後に署名する。
param(
    [string]$IdentityName = 'Kadomaru.TejunRecorder',
    [string]$Publisher = 'CN=00000000-0000-0000-0000-000000000000',
    [string]$PublisherDisplayName = 'kadomaru',
    [string]$DisplayName = '手順レコーダー',
    [string]$Version = '',
    [string]$OutDir = 'publish-store'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = Resolve-Path (Join-Path $here '..\..')
$project = Join-Path $repo 'src\Tejun.App'

if (-not $Version) {
    $Version = (dotnet msbuild $project -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'バージョンを読み取れませんでした' }
}
$parts = ($Version -split '[-+]')[0] -split '\.'
if ($parts.Count -lt 3) { throw "バージョンは 1.2.3 の形で指定してください: $Version" }
# Store の決まり：1 つ目は 0 にできず、4 つ目は 0 のままにする
if ([int]$parts[0] -eq 0) { throw "Store は 0 で始まるバージョンを受け付けません: $Version" }
$msixVersion = "$([int]$parts[0]).$([int]$parts[1]).$([int]$parts[2]).0"

function Find-SdkTool([string]$name) {
    $tool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64\$name" -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Directory.Parent.Name } | Select-Object -Last 1
    if (-not $tool) { throw "$name が見つかりません。Windows SDK を入れてください" }
    $tool.FullName
}
$makeappx = Find-SdkTool 'makeappx.exe'
$makepri = Find-SdkTool 'makepri.exe'

$out = Join-Path $repo $OutDir
$layout = Join-Path $out 'layout'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item $layout -ItemType Directory | Out-Null

# パッケージの中では 1 つの exe にまとめない（起動のたびに展開する必要がなくなる）
dotnet publish $project -c Release -r win-x64 -o $layout `
    -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:EnableCompressionInSingleFile=false -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish に失敗しました' }
# 画像ライブラリに付いてくるデバッグ用のファイルは配布に要らない（100 MB ほどある）
Get-ChildItem $layout -Filter *.pdb -Recurse | Remove-Item -Force

Copy-Item (Join-Path $here 'Assets') (Join-Path $layout 'Assets') -Recurse

$esc = { param($s) [System.Security.SecurityElement]::Escape($s) }
$manifest = Get-Content (Join-Path $here 'AppxManifest.xml') -Raw -Encoding utf8
$manifest = $manifest.Replace('__IDENTITY_NAME__', (& $esc $IdentityName)).
    Replace('__PUBLISHER_DISPLAY_NAME__', (& $esc $PublisherDisplayName)).
    Replace('__PUBLISHER__', (& $esc $Publisher)).
    Replace('__DISPLAY_NAME__', (& $esc $DisplayName)).
    Replace('__VERSION__', $msixVersion)
[System.IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, [System.Text.UTF8Encoding]::new($false))

# アイコンの大きさ違い（タスク バー用など）を Windows が選べるように、索引 (resources.pri) を作る
$priConfig = Join-Path $out 'priconfig.xml'
& $makepri createconfig /cf $priConfig /dq ja-JP /o
if ($LASTEXITCODE -ne 0) { throw 'makepri createconfig に失敗しました' }
# 言語や倍率ごとに索引を分ける設定を外し、1 つの resources.pri にまとめる
$xml = [xml](Get-Content $priConfig -Raw)
foreach ($node in @($xml.resources.SelectNodes('packaging'))) { [void]$xml.resources.RemoveChild($node) }
$xml.Save($priConfig)
& $makepri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o
if ($LASTEXITCODE -ne 0) { throw 'makepri new に失敗しました' }

$msix = Join-Path $out "TejunRecorder_${msixVersion}_x64.msix"
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx に失敗しました' }

Write-Host "できました: $msix"
