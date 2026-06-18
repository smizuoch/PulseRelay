[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $IdentityFile,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+\.0$')]
    [string] $Version,

    [string] $Configuration = "Release",

    [ValidateSet("win-x64")]
    [string] $Runtime = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-LastExitCode {
    param([Parameter(Mandatory)][string] $Operation)

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Get-RequiredJsonValue {
    param(
        [Parameter(Mandatory)] [object] $Json,
        [Parameter(Mandatory)] [string] $PropertyName
    )

    $property = $Json.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string] $property.Value)) {
        throw "Identity file is missing a non-empty '$PropertyName' value."
    }

    return ([string] $property.Value).Trim()
}

function Write-ResizedPng {
    param(
        [Parameter(Mandatory)] [string] $SourcePath,
        [Parameter(Mandatory)] [string] $DestinationPath,
        [Parameter(Mandatory)] [int] $Width,
        [Parameter(Mandatory)] [int] $Height
    )

    $source = [System.Drawing.Image]::FromFile($SourcePath)
    try {
        $destination = [System.Drawing.Bitmap]::new(
            $Width,
            $Height,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $destination.SetResolution(96, 96)
            $graphics = [System.Drawing.Graphics]::FromImage($destination)
            try {
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.DrawImage($source, 0, 0, $Width, $Height)
            }
            finally {
                $graphics.Dispose()
            }

            $destination.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $destination.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}

function Find-MakeAppx {
    $command = Get-Command "MakeAppx.exe" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $kitsRoot)) {
        throw "Windows SDK was not found at '$kitsRoot'."
    }

    $candidates = Get-ChildItem -Path $kitsRoot -Filter "MakeAppx.exe" -File -Recurse |
        Where-Object { $_.FullName -match '[\\/]x64[\\/]MakeAppx\.exe$' } |
        Sort-Object -Property FullName -Descending

    $candidate = $candidates | Select-Object -First 1
    if ($null -eq $candidate) {
        throw "MakeAppx.exe was not found in the installed Windows SDK."
    }

    return $candidate.FullName
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resolvedIdentityFile = (Resolve-Path -LiteralPath $IdentityFile).Path
$manifestTemplate = Join-Path $repositoryRoot "packaging\msix\AppxManifest.template.xml"
$projectPath = Join-Path $repositoryRoot "src\PulseRelay.Desktop\PulseRelay.Desktop.csproj"
$sourceLogo = Join-Path $repositoryRoot "src\PulseRelay.Desktop\Assets\PulseRelay.png"
$storeRoot = Join-Path $repositoryRoot "artifacts\store"
$payloadDirectory = Join-Path $storeRoot "payload"
$stagingDirectory = Join-Path $storeRoot "staging"
$inspectDirectory = Join-Path $storeRoot "inspect"
$assetsDirectory = Join-Path $stagingDirectory "Assets"
$packagePath = Join-Path $storeRoot "PulseRelay_${Version}_x64.msix"
$checksumPath = Join-Path $storeRoot "SHA256SUMS.txt"

foreach ($requiredPath in @($manifestTemplate, $projectPath, $sourceLogo)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required file was not found: $requiredPath"
    }
}

$versionParts = $Version.Split(".") | ForEach-Object { [int] $_ }
$outOfRangeVersionParts = @($versionParts | Where-Object { $_ -gt 65535 })
if ($versionParts[0] -eq 0 -or $outOfRangeVersionParts.Count -gt 0) {
    throw "Version components must be between 0 and 65535, and the first component must be greater than 0."
}

$identity = Get-Content -LiteralPath $resolvedIdentityFile -Raw | ConvertFrom-Json
$packageIdentityName = Get-RequiredJsonValue -Json $identity -PropertyName "PackageIdentityName"
$publisher = Get-RequiredJsonValue -Json $identity -PropertyName "Publisher"
$publisherDisplayName = Get-RequiredJsonValue -Json $identity -PropertyName "PublisherDisplayName"

if (Test-Path -LiteralPath $storeRoot) {
    Remove-Item -LiteralPath $storeRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $payloadDirectory, $stagingDirectory, $inspectDirectory, $assetsDirectory -Force |
    Out-Null

$publishArguments = @(
    "publish",
    $projectPath,
    "--configuration", $Configuration,
    "--framework", "net10.0-windows10.0.19041.0",
    "--runtime", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "--output", $payloadDirectory
)

Write-Host "Publishing PulseRelay desktop payload..."
& dotnet @publishArguments
Assert-LastExitCode -Operation "dotnet publish"

Copy-Item -Path (Join-Path $payloadDirectory "*") -Destination $stagingDirectory -Recurse -Force

Add-Type -AssemblyName System.Drawing
Write-Host "Preparing package logo sizes from src/PulseRelay.Desktop/Assets/PulseRelay.png..."
Write-ResizedPng -SourcePath $sourceLogo -DestinationPath (Join-Path $assetsDirectory "StoreLogo.png") -Width 50 -Height 50
Write-ResizedPng -SourcePath $sourceLogo -DestinationPath (Join-Path $assetsDirectory "Square44x44Logo.png") -Width 44 -Height 44
Write-ResizedPng -SourcePath $sourceLogo -DestinationPath (Join-Path $assetsDirectory "Square150x150Logo.png") -Width 150 -Height 150
Copy-Item `
    -LiteralPath (Join-Path $assetsDirectory "Square44x44Logo.png") `
    -Destination (Join-Path $assetsDirectory "Square44x44Logo.targetsize-44_altform-unplated.png")

$xmlDocument = [System.Xml.XmlDocument]::new()
$xmlDocument.PreserveWhitespace = $true
$xmlDocument.Load($manifestTemplate)

$namespaceManager = [System.Xml.XmlNamespaceManager]::new($xmlDocument.NameTable)
$namespaceManager.AddNamespace("foundation", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")

$identityNode = $xmlDocument.SelectSingleNode("/foundation:Package/foundation:Identity", $namespaceManager)
$publisherDisplayNameNode = $xmlDocument.SelectSingleNode(
    "/foundation:Package/foundation:Properties/foundation:PublisherDisplayName",
    $namespaceManager)

if ($null -eq $identityNode -or $null -eq $publisherDisplayNameNode) {
    throw "Manifest template does not contain the required identity nodes."
}

$identityNode.SetAttribute("Name", $packageIdentityName)
$identityNode.SetAttribute("Publisher", $publisher)
$identityNode.SetAttribute("Version", $Version)
$publisherDisplayNameNode.InnerText = $publisherDisplayName

$manifestPath = Join-Path $stagingDirectory "AppxManifest.xml"
$xmlSettings = [System.Xml.XmlWriterSettings]::new()
$xmlSettings.Encoding = [System.Text.UTF8Encoding]::new($false)
$xmlSettings.Indent = $true
$xmlSettings.NewLineChars = "`r`n"
$xmlSettings.NewLineHandling = [System.Xml.NewLineHandling]::Replace

$xmlWriter = [System.Xml.XmlWriter]::Create($manifestPath, $xmlSettings)
try {
    $xmlDocument.Save($xmlWriter)
}
finally {
    $xmlWriter.Dispose()
}

$makeAppx = Find-MakeAppx
Write-Host "Using MakeAppx: $makeAppx"

& $makeAppx pack /d $stagingDirectory /p $packagePath /o
Assert-LastExitCode -Operation "MakeAppx pack"

& $makeAppx unpack /p $packagePath /d $inspectDirectory /o
Assert-LastExitCode -Operation "MakeAppx unpack"

$inspectedManifestPath = Join-Path $inspectDirectory "AppxManifest.xml"
$inspectedManifest = [xml] (Get-Content -LiteralPath $inspectedManifestPath -Raw)
if ($inspectedManifest.Package.Identity.Name -ne $packageIdentityName) {
    throw "Packed manifest identity name does not match the requested Store identity."
}
if ($inspectedManifest.Package.Identity.Publisher -ne $publisher) {
    throw "Packed manifest publisher does not match the requested Store identity."
}
if ($inspectedManifest.Package.Identity.Version -ne $Version) {
    throw "Packed manifest version does not match '$Version'."
}

$requiredPackageFiles = @(
    "PulseRelay.Desktop.exe",
    "THIRD-PARTY-NOTICES.txt",
    "Assets\StoreLogo.png",
    "Assets\Square44x44Logo.png",
    "Assets\Square150x150Logo.png",
    "Assets\Square44x44Logo.targetsize-44_altform-unplated.png"
)

foreach ($relativePath in $requiredPackageFiles) {
    $packageFile = Join-Path $inspectDirectory $relativePath
    if (-not (Test-Path -LiteralPath $packageFile -PathType Leaf)) {
        throw "Required file is missing from the MSIX: $relativePath"
    }
}

$forbiddenExtensions = @(".pdb", ".pfx", ".cer", ".key", ".log")
$forbiddenNames = @(".env", "store-identity.json")
$forbiddenFiles = @(
    Get-ChildItem -LiteralPath $inspectDirectory -Recurse -File |
        Where-Object {
            $forbiddenExtensions -contains $_.Extension.ToLowerInvariant() -or
            $forbiddenNames -contains $_.Name.ToLowerInvariant()
        }
)

if ($forbiddenFiles.Count -gt 0) {
    $fileList = ($forbiddenFiles.FullName | ForEach-Object {
        [System.IO.Path]::GetRelativePath($inspectDirectory, $_)
    }) -join ", "
    throw "Forbidden files were found in the MSIX: $fileList"
}

$textExtensions = @(".config", ".json", ".txt", ".xml")
$pathLeaks = @(
    Get-ChildItem -LiteralPath $inspectDirectory -Recurse -File |
        Where-Object { $textExtensions -contains $_.Extension.ToLowerInvariant() } |
        Select-String -Pattern 'C:\Users\', '/Users/' -SimpleMatch
)

if ($pathLeaks.Count -gt 0) {
    $leakFiles = ($pathLeaks.Path | Sort-Object -Unique) -join ", "
    throw "Local user paths were found in packaged text files: $leakFiles"
}

$hash = Get-FileHash -LiteralPath $packagePath -Algorithm SHA256
"$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($packagePath))" |
    Set-Content -LiteralPath $checksumPath -Encoding utf8

Write-Host ""
Write-Host "Store MSIX created successfully:"
Write-Host "  Package: $packagePath"
Write-Host "  SHA-256: $($hash.Hash)"
