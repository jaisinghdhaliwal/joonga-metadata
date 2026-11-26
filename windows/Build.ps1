[CmdletBinding()]
param(
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64',
    [switch]$SkipTests,
    [switch]$SkipInstaller,
    [string]$ExifToolFolder
)
$ErrorActionPreference = 'Stop'
if ($ExifToolFolder) {
    $ExifToolFolder = (Resolve-Path -LiteralPath $ExifToolFolder).Path
}
Push-Location $PSScriptRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 and run Build.ps1 again.' }
    $output = Join-Path $PSScriptRoot "build/$Runtime/Joonga Tag"
    if (-not $ExifToolFolder) {
        $vendor = Join-Path $PSScriptRoot 'build/vendor'
        New-Item -ItemType Directory -Force $vendor | Out-Null
        $archive = Join-Path $vendor 'exiftool-13.64_64.zip'
        if (-not (Test-Path $archive)) {
            if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
                & curl.exe -fsSL 'https://sourceforge.net/projects/exiftool/files/exiftool-13.64_64.zip/download' -o $archive
                if ($LASTEXITCODE -ne 0) { throw 'ExifTool download failed.' }
            } else {
                Invoke-WebRequest 'https://sourceforge.net/projects/exiftool/files/exiftool-13.64_64.zip/download' -UserAgent 'curl/8.0' -OutFile $archive
            }
        }
        $expected = '72c29c44d1e860eebcaefa71692d979af9858fe9a6d7d791a5e7f1dac77804af'
        if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'ExifTool download checksum mismatch. Remove the cached ZIP and retry.' }
        Expand-Archive $archive -DestinationPath $vendor -Force
        $ExifToolFolder = Join-Path $vendor 'exiftool-13.64_64'
    }
    $supportFiles = Join-Path $ExifToolFolder 'exiftool_files'
    $executable = @('exiftool.exe', 'exiftool(-k).exe') | Where-Object {
        Test-Path -LiteralPath (Join-Path $ExifToolFolder $_) -PathType Leaf
    } | Select-Object -First 1
    if (-not $executable -or -not (Test-Path -LiteralPath $supportFiles -PathType Container)) {
        throw 'The complete ExifTool distribution, including its executable and exiftool_files, is required.'
    }
    dotnet publish MetadataTransfer.Windows/MetadataTransfer.Windows.csproj -c Release -r $Runtime --self-contained true -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Windows app build failed.' }
    $tools = Join-Path $output 'tools'
    New-Item -ItemType Directory -Force $tools | Out-Null
    Get-ChildItem -LiteralPath $ExifToolFolder -Force | Where-Object {
        $_.Name -notin @('exiftool.exe', 'exiftool(-k).exe')
    } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $tools -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $ExifToolFolder $executable) -Destination (Join-Path $tools 'exiftool.exe') -Force
    $docs = Join-Path $PSScriptRoot '../docs/windows'
    $releaseDocs = @('README.md', 'THIRD-PARTY-NOTICES.md', 'WINDOWS-QA.md', 'VALIDATION.md')
    foreach ($file in $releaseDocs) {
        Copy-Item -LiteralPath (Join-Path $docs $file) -Destination $output -Force
    }
    if (-not $SkipTests) {
        dotnet run --project MetadataTransfer.Tests -c Release -- (Join-Path $tools 'exiftool.exe')
        if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }
    }
    $manifest = Join-Path $output 'JoongaTag.install-files.txt'
    $relativeFiles = Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.FullName -ne $manifest } |
        ForEach-Object { [System.IO.Path]::GetRelativePath($output, $_.FullName) }
    Set-Content -LiteralPath $manifest -Value $relativeFiles -Encoding utf8
    $zip = Join-Path $PSScriptRoot "build/Joonga-Tag-Windows-${Runtime}.zip"
    Compress-Archive -Path $output -DestinationPath $zip -Force
    Write-Host "Ready: $zip"
    if (-not $SkipInstaller) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $payloadZip = Join-Path $PSScriptRoot "build/$Runtime/payload.zip"
        if (Test-Path -LiteralPath $payloadZip) { Remove-Item -LiteralPath $payloadZip -Force }
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $output,
            $payloadZip,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)
        $installerOut = Join-Path $PSScriptRoot "build/$Runtime/installer"
        dotnet publish MetadataTransfer.Installer/MetadataTransfer.Installer.csproj -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true "-p:InstallerPayloadPath=$payloadZip" -o $installerOut
        if ($LASTEXITCODE -ne 0) { throw 'Windows installer build failed.' }
        $setupExe = Join-Path $PSScriptRoot "build/Joonga-Tag-Windows-${Runtime}-Setup.exe"
        Copy-Item -LiteralPath (Join-Path $installerOut 'Joonga Tag Setup.exe') -Destination $setupExe -Force
        Write-Host "Ready: $setupExe"
    }
} finally { Pop-Location }
