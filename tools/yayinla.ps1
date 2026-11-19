# Yeni sürüm yayınlar: sürümü artırır, programı derler, kodu GitHub'a gönderir ve MetroKafeLANViewer.zip'i
# GitHub sürümü olarak yükler. Açık programlar 6 saat içinde (ya da Ayarlar → Şimdi denetle ile) yeni sürümü görür.
#
#   yayinla.bat              -> 1.0.3 -> 1.0.4
#   yayinla.bat 1.2.0        -> belirtilen sürüm
#   -Notlar "..."            -> değişiklik notunu sormadan kullanır
param([string]$Surum, [string]$Notlar)

# 'Stop' değil: Windows PowerShell, git/gh/dotnet'in stderr'e yazdığı normal mesajları da hata sayıp durur.
$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

function Adim($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Dur($text) { Write-Host "`nHATA: $text" -ForegroundColor Red; exit 1 }

$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Dur "GitHub CLI (gh) kurulu değil: winget install --id GitHub.cli" }
gh auth status *> $null
if ($LASTEXITCODE -ne 0) { Dur "GitHub'a giriş yapılmamış: gh auth login" }

# ---------------------------------------------------------------- sürüm
$props = Join-Path $root 'Directory.Build.props'
$xml = [IO.File]::ReadAllText($props)
$eski = [regex]::Match($xml, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $Surum) {
    $v = [version]$eski
    $Surum = "$($v.Major).$($v.Minor).$($v.Build + 1)"
}
if (-not ($Surum -match '^\d+\.\d+\.\d+$')) { Dur "Sürüm 1.2.3 biçiminde olmalı: $Surum" }
$tag = "v$Surum"
gh release view $tag *> $null
if ($LASTEXITCODE -eq 0) { Dur "$tag zaten yayınlanmış." }
# Aynı numara yalnızca hiç yayınlanmamışsa (ilk sürüm) kullanılabilir
if ([version]$Surum -lt [version]$eski) { Dur "Yeni sürüm ($Surum) mevcut sürümden ($eski) küçük olamaz." }

Write-Host "Sürüm: $eski -> $Surum" -ForegroundColor Green
$notlar = if ($PSBoundParameters.ContainsKey('Notlar')) { $Notlar } else { Read-Host "Bu sürümde neler değişti? (boş bırakılabilir)" }

# ---------------------------------------------------------------- derleme
$out = Join-Path $root "yayin\$Surum"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null

[IO.File]::WriteAllText($props, $xml.Replace("<Version>$eski</Version>", "<Version>$Surum</Version>"))
try {
    Adim "MetroKafeLANViewer derleniyor"
    $dir = Join-Path $out 'MetroKafeLANViewer'
    dotnet publish MetroKafeLANViewer.csproj -c Release -o $dir --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Derlenemedi." }
    $exe = Join-Path $dir 'MetroKafeLANViewer.exe'
    $ver = (Get-Item $exe).VersionInfo.ProductVersion
    if ($ver -ne $Surum) { throw "Exe sürümü $ver, beklenen $Surum." }
    Adim "MetroKafeLANViewer.zip hazırlanıyor"
    Compress-Archive -Path $exe -DestinationPath (Join-Path $out 'MetroKafeLANViewer.zip') -CompressionLevel Optimal -ErrorAction Stop
}
catch {
    [IO.File]::WriteAllText($props, $xml)
    Dur $_
}

# ---------------------------------------------------------------- GitHub
Adim "Kod GitHub'a gönderiliyor"
git add -A
git commit -q -m "Sürüm $Surum$(if ($notlar) { "`n`n$notlar" })"
git tag $tag
function Dene($islem) {
    for ($i = 1; $i -le 3; $i++) {
        & $islem
        if ($LASTEXITCODE -eq 0) { return $true }
        if ($i -lt 3) { Write-Host "Tekrar deneniyor ($i/3)..." -ForegroundColor Yellow; Start-Sleep 10 }
    }
    return $false
}
if (-not (Dene { git push -q origin HEAD $tag })) {
    Dur "git push başarısız (commit ve $tag etiketi hazır). İnterneti kontrol edip: git push origin HEAD $tag"
}

Adim "Sürüm $tag yükleniyor"
$zip = Join-Path $out 'MetroKafeLANViewer.zip'
$baslik = "Sürüm $Surum"
$not = if ($notlar) { $notlar } else { $baslik }
if (-not (Dene { gh release view $tag *> $null; if ($LASTEXITCODE -eq 0) { gh release upload $tag $zip --clobber } else { gh release create $tag $zip --title $baslik --notes $not } })) {
    Dur "Sürüm yüklenemedi. Tekrar denemek için: gh release upload $tag yayin\$Surum\MetroKafeLANViewer.zip --clobber"
}

Write-Host "`nTAMAM: $tag yayınlandı." -ForegroundColor Green
Write-Host "Derlenen dosya: $exe"
