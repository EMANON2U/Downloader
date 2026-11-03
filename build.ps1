param([string]$Version = '1.7.0',[string]$ReportUrl = '', [string]$SupportContact = 'contato.resoup@gmail.com')
$ErrorActionPreference = 'Stop'

$Product = 'Downloader'
$Company = 'ArchiveByLOPES'

if (-not (Select-String -Path "$PSScriptRoot\CHANGELOG.md" -SimpleMatch "## $Version |" -Quiet)) { throw "CHANGELOG.md nao tem a secao '## $Version | AAAA-MM-DD'" }

$root = $PSScriptRoot
$fw   = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$obj  = "$root\obj"
$rel  = "$root\dist\release"
New-Item -ItemType Directory -Force "$obj\bin", $rel | Out-Null

function Sha([string]$f) { (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash }

$payload = @()
foreach ($name in 'yt-dlp.exe', 'ffmpeg.exe', 'deno.exe') {
    $src = "$root\deps\$name"
    if (-not (Test-Path $src)) { throw "Falta deps\$name" }
    $hash = Sha $src
    $gz = "$obj\bin\$name.gz"
    if (-not (Test-Path $gz) -or (Get-Content "$gz.sha" -ErrorAction SilentlyContinue) -ne $hash) {
        Write-Host "Comprimindo $name ..."
        $in = [IO.File]::OpenRead($src)
        $out = [IO.File]::Create($gz)
        $z = New-Object IO.Compression.GZipStream($out, [IO.Compression.CompressionLevel]::Optimal)
        $in.CopyTo($z, 1MB); $z.Dispose(); $out.Dispose(); $in.Dispose()
        Set-Content "$gz.sha" $hash
    }
    $payload += ,@($name, $hash, (Get-Item $src).Length)
}

$ytv = (& "$root\deps\yt-dlp.exe" --version).Trim()
$ffv = ((& "$root\deps\ffmpeg.exe" -hide_banner -version | Select-Object -First 1) -replace '^ffmpeg version (\S+).*', '$1')
$dnv = ((& "$root\deps\deno.exe" --version | Select-Object -First 1) -replace '^deno (\S+).*', '$1')
$notices = (Get-Content "$root\THIRD-PARTY-NOTICES.txt" -Raw -Encoding UTF8).
    Replace('{{YTDLP_VERSION}}', $ytv).Replace('{{FFMPEG_VERSION}}', $ffv).Replace('{{DENO_VERSION}}', $dnv) +
    (Get-Content "$root\deps\ffmpeg-LICENSE.txt" -Raw)
[IO.File]::WriteAllText("$obj\THIRD-PARTY-NOTICES.txt", $notices, (New-Object Text.UTF8Encoding $true))

$rows = ($payload | ForEach-Object { "            new[] { `"$($_[0])`", `"$($_[1])`", `"$($_[2])`" }" }) -join ",`r`n"
@"
namespace Downloader
{
    public static class Version
    {
        public const string Product = "$Product";
        public const string Company = "$Company";
        public const string Number = "$Version";
        public const string Year = "$((Get-Date).Year)";
        public const string ReportUrl = "$ReportUrl";
        public const string SupportContact = "$SupportContact";
        public static readonly string[][] Payload = {
$rows };
    }
}
"@ | Set-Content "$obj\Version.cs" -Encoding UTF8

$exe = "$rel\Downloader.exe"
$res = @("/resource:$root\src\MainWindow.xaml,MainWindow.xaml", "/resource:$obj\THIRD-PARTY-NOTICES.txt,THIRD-PARTY-NOTICES.txt",
         "/resource:$root\src\ajuda.md,ajuda.md", "/resource:$root\src\legal.md,legal.md","/resource:$root\CHANGELOG.md,CHANGELOG.md")
foreach ($p in $payload) { $res += "/resource:$obj\bin\$($p[0]).gz,bin/$($p[0]).gz" }
& "$fw\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /utf8output /out:$exe `
    /win32icon:"$root\icon\downloader.ico" /win32manifest:"$root\src\app.manifest" `
    $res `
    /r:"$fw\WPF\PresentationFramework.dll" /r:"$fw\WPF\PresentationCore.dll" /r:"$fw\WPF\WindowsBase.dll" `
    /r:System.Xaml.dll /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll `
    "$root\src\App.cs" "$root\src\Setup.cs" "$root\src\Errors.cs" "$root\src\Estimate.cs" "$root\src\Music.cs" "$root\src\Browsers.cs" "$obj\Version.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao' }
Copy-Item "$obj\THIRD-PARTY-NOTICES.txt" $rel -Force
Write-Host ("OK: $exe ({0:N1} MB)" -f ((Get-Item $exe).Length / 1MB))

"$(Sha $exe)  Downloader.exe" | Set-Content "$rel\SHA256SUMS.txt" -Encoding ASCII
Write-Host "SHA-256: $(Sha $exe)"
