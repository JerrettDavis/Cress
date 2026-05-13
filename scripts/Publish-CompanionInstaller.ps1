param(
    [string]$Version = '0.1.0-local',
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$PublishDirectory = 'artifacts\companion-publish',
    [string]$PackageDirectory = 'artifacts\packages'
)

$ErrorActionPreference = 'Stop'

function Resolve-FullPath {
    param([string]$Path)

    $combined = [System.IO.Path]::Combine((Get-Location).Path, $Path)
    return [System.IO.Path]::GetFullPath($combined)
}

function Get-RelativePath {
    param(
        [string]$RootPath,
        [string]$TargetPath
    )

    $rootUri = New-Object System.Uri(($RootPath.TrimEnd('\') + '\'))
    $targetUri = New-Object System.Uri($TargetPath)
    return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

function Get-InstallerProductVersion {
    param([string]$SemanticVersion)

    $normalized = $SemanticVersion.Trim()
    if ($normalized.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring(1)
    }

    $normalized = $normalized.Split('-', 2, [System.StringSplitOptions]::None)[0]
    $normalized = $normalized.Split('+', 2, [System.StringSplitOptions]::None)[0]

    if (-not [System.Text.RegularExpressions.Regex]::IsMatch($normalized, '^\d+\.\d+\.\d+$')) {
        throw "Version '$SemanticVersion' must start with a semantic version like 1.2.3."
    }

    return $normalized
}

function Get-StableWixId {
    param(
        [string]$Prefix,
        [string]$RelativePath
    )

    $sanitized = [System.Text.RegularExpressions.Regex]::Replace($RelativePath, '[^A-Za-z0-9]+', '_').Trim('_')
    if ([string]::IsNullOrWhiteSpace($sanitized)) {
        $sanitized = 'Root'
    }

    if ($sanitized.Length -gt 40) {
        $sanitized = $sanitized.Substring(0, 40)
    }

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($RelativePath.ToLowerInvariant()))
    }
    finally {
        $sha.Dispose()
    }
    $hash = [System.BitConverter]::ToString($hashBytes).Replace('-', '').Substring(0, 12)
    return "${Prefix}_${sanitized}_${hash}"
}

function New-CompanionInstallerFragment {
    param(
        [string]$PublishRoot,
        [string]$OutputFile
    )

    $publishRootInfo = Get-Item -LiteralPath $PublishRoot
    $publishRootPath = $publishRootInfo.FullName
    $directoryIds = @{}

    function Write-DirectoryNodes {
        param(
            [System.Xml.XmlWriter]$Writer,
            [string]$ParentPath
        )

        foreach ($directory in (Get-ChildItem -LiteralPath $ParentPath -Directory | Sort-Object Name)) {
            $relativeDirectory = Get-RelativePath -RootPath $publishRootPath -TargetPath $directory.FullName
            $directoryId = Get-StableWixId -Prefix 'Dir' -RelativePath $relativeDirectory
            $directoryIds[$relativeDirectory] = $directoryId

            $Writer.WriteStartElement('Directory', 'http://wixtoolset.org/schemas/v4/wxs')
            $Writer.WriteAttributeString('Id', $directoryId)
            $Writer.WriteAttributeString('Name', $directory.Name)
            Write-DirectoryNodes -Writer $Writer -ParentPath $directory.FullName
            $Writer.WriteEndElement()
        }
    }

    $outputDirectory = Split-Path -Parent $OutputFile
    if (-not (Test-Path $outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory | Out-Null
    }

    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)

    $writer = [System.Xml.XmlWriter]::Create($OutputFile, $settings)

    try {
        $writer.WriteStartDocument()
        $writer.WriteStartElement('Wix', 'http://wixtoolset.org/schemas/v4/wxs')

        $writer.WriteStartElement('Fragment')
        $writer.WriteStartElement('DirectoryRef')
        $writer.WriteAttributeString('Id', 'INSTALLDIR')
        Write-DirectoryNodes -Writer $writer -ParentPath $publishRootPath
        $writer.WriteEndElement()
        $writer.WriteEndElement()

        $writer.WriteStartElement('Fragment')
        $writer.WriteStartElement('ComponentGroup')
        $writer.WriteAttributeString('Id', 'CompanionApplicationFiles')

        foreach ($file in (Get-ChildItem -LiteralPath $publishRootPath -File -Recurse | Sort-Object FullName)) {
            $relativeFile = Get-RelativePath -RootPath $publishRootPath -TargetPath $file.FullName
            $relativeDirectory = Split-Path $relativeFile -Parent
            $directoryId = if ([string]::IsNullOrWhiteSpace($relativeDirectory)) { 'INSTALLDIR' } else { $directoryIds[$relativeDirectory] }
            $componentId = Get-StableWixId -Prefix 'Component' -RelativePath $relativeFile
            $fileId = Get-StableWixId -Prefix 'File' -RelativePath $relativeFile

            $writer.WriteStartElement('Component')
            $writer.WriteAttributeString('Id', $componentId)
            $writer.WriteAttributeString('Directory', $directoryId)
            $writer.WriteAttributeString('Guid', '*')
            $writer.WriteAttributeString('Bitness', 'always64')

            $writer.WriteStartElement('File')
            $writer.WriteAttributeString('Id', $fileId)
            $writer.WriteAttributeString('Source', $file.FullName)
            $writer.WriteAttributeString('KeyPath', 'yes')
            $writer.WriteEndElement()

            $writer.WriteEndElement()
        }

        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    }
    finally {
        $writer.Dispose()
    }
}

$packageVersionLabel = $Version.Trim()
if ($packageVersionLabel.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
    $packageVersionLabel = $packageVersionLabel.Substring(1)
}

$installerProductVersion = Get-InstallerProductVersion -SemanticVersion $packageVersionLabel
$publishPath = Resolve-FullPath -Path $PublishDirectory
$packagePath = Resolve-FullPath -Path $PackageDirectory
$installerProject = 'installer\Cress.Companion.Installer\Cress.Companion.Installer.wixproj'
$generatedFragment = Resolve-FullPath -Path 'installer\Cress.Companion.Installer\Generated\CompanionFiles.g.wxs'
$publishDirectoryWithSlash = if ($publishPath.EndsWith('\', [System.StringComparison]::Ordinal)) { $publishPath } else { "$publishPath\" }

if (Test-Path $publishPath) {
    Remove-Item -Path $publishPath -Recurse -Force
}

if (-not (Test-Path $packagePath)) {
    New-Item -ItemType Directory -Path $packagePath | Out-Null
}

$publishArgs = @(
    'publish',
    'src\Cress.Companion.Windows\Cress.Companion.Windows.csproj',
    '--configuration', $Configuration,
    '--runtime', $Runtime,
    '--self-contained', 'true',
    '-p:PublishReadyToRun=true',
    "--output", $publishPath,
    "/p:Version=$packageVersionLabel"
)

Write-Host "Publishing desktop companion $packageVersionLabel for $Runtime..."
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed for the desktop companion.'
}

$portableZip = Join-Path $packagePath ("Cress.DesktopCompanion-$Runtime-$packageVersionLabel.zip")
if (Test-Path $portableZip) {
    Remove-Item -Path $portableZip -Force
}

Write-Host "Creating portable zip $portableZip..."
Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $portableZip -Force

Write-Host "Generating WiX file manifest..."
New-CompanionInstallerFragment -PublishRoot $publishPath -OutputFile $generatedFragment

$installerArgs = @(
    'build',
    $installerProject,
    '--configuration', $Configuration,
    "-p:CompanionPublishDir=$publishDirectoryWithSlash",
    "-p:PackageVersionLabel=$packageVersionLabel",
    "-p:InstallerProductVersion=$installerProductVersion"
)

Write-Host "Building MSI installer..."
& dotnet @installerArgs
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet build failed for the companion installer.'
}

$installerOutput = Get-ChildItem -Path 'installer\Cress.Companion.Installer\bin' -Recurse -Filter '*.msi' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $installerOutput) {
    throw 'MSI output was not produced by the companion installer build.'
}

Write-Host "Portable zip: $portableZip"
Write-Host "MSI installer: $($installerOutput.FullName)"

if ($env:GITHUB_OUTPUT) {
    "portable_zip=$portableZip" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding ascii
    "msi_path=$($installerOutput.FullName)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding ascii
    "package_version=$packageVersionLabel" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding ascii
    "installer_version=$installerProductVersion" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding ascii
}
