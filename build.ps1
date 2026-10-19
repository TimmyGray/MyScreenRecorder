$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = $PSScriptRoot
$tools = Join-Path $root 'tools'
$ffmpeg = Join-Path $tools 'ffmpeg.exe'
$font = Join-Path $root 'src\MyScreenRecorder\Assets\PressStart2P-Regular.ttf'

New-Item -ItemType Directory -Force $tools | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if (-not (Test-Path $ffmpeg)) {
    $zip = Join-Path $tools 'ffmpeg.zip'
    $extract = Join-Path $tools 'ffmpeg_extract'
    Write-Host 'Downloading ffmpeg...'
    Invoke-WebRequest -Uri 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $extract -Force
    $found = Get-ChildItem -Path $extract -Recurse -Filter ffmpeg.exe | Select-Object -First 1
    if (-not $found) { throw 'ffmpeg.exe not found in archive' }
    Copy-Item $found.FullName $ffmpeg -Force
    Remove-Item $zip -Force
    Remove-Item $extract -Recurse -Force
}

if (-not (Test-Path $font)) {
    Write-Host 'Downloading font...'
    try {
        Invoke-WebRequest -Uri 'https://github.com/google/fonts/raw/main/ofl/pressstart2p/PressStart2P-Regular.ttf' -OutFile $font -UseBasicParsing
    } catch {
        Write-Warning 'Font download failed; the UI will fall back to Consolas.'
        if (Test-Path $font) { Remove-Item $font -Force }
    }
}

dotnet publish (Join-Path $root 'src\MyScreenRecorder') -c Release -o (Join-Path $root 'dist')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Write-Host ('Built: ' + (Join-Path $root 'dist\MyScreenRecorder.exe'))
