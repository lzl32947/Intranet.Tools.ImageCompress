param([string]$Version = '1.0.0.0', [string]$CertificateThumbprint)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$workDir = Join-Path $root 'artifacts'
$compileDir = Join-Path $workDir 'compile'
$appDir = Join-Path $workDir ('layout-' + [Guid]::NewGuid().ToString('N'))
$shellDir = Join-Path $workDir 'shell'
$dist = Join-Path $workDir 'distribution'
$projectName = 'Intranet.Tools.ImageCompress'
[IO.Directory]::CreateDirectory($dist) | Out-Null
function Assert-Native([string]$step) { if ($LASTEXITCODE -ne 0) { throw "$step failed ($LASTEXITCODE)" } }
$kit = Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' KitsRoot10
$sdk = Get-ChildItem -LiteralPath (Join-Path $kit 'bin') -Directory |
    Where-Object { $_.Name -match '^\d+\.' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'x64\makeappx.exe')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$sdk) { throw 'Windows SDK x64 packaging tools are required.' }
$tools = Join-Path $sdk.FullName 'x64'
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Assets\ImagePress.ico'))) { & (Join-Path $PSScriptRoot 'New-Assets.ps1') }
& dotnet publish (Join-Path $root 'src\Desktop\Desktop.csproj') -c Release -r win-x64 --self-contained true --artifacts-path $compileDir -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false -o $appDir
Assert-Native 'WPF publish'
& dotnet publish (Join-Path $root 'src\Shell\Shell.csproj') -c Release -r win-x64 --artifacts-path $compileDir -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:NativeDebugSymbols=false -o $shellDir
Assert-Native 'Native AOT publish'

# A fresh staging folder excludes obsolete files from earlier packages.
$stage = $appDir
Copy-Item -LiteralPath (Join-Path $shellDir 'ImagePressShell.dll') -Destination $stage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets') -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
$manifestPath = Join-Path $stage 'AppxManifest.xml'
$manifest = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'AppxManifest.xml'))
[xml]$xml = $manifest
$xml.Package.Identity.Version = $Version
$xml.Save($manifestPath)
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages' }
Copy-Item -LiteralPath (Join-Path $packages 'magick.net-q16-x64\15.1.1\Notice.txt') -Destination (Join-Path $stage 'Magick.NET-NOTICE.txt')
$runtime = (Get-Content -Raw -LiteralPath (Join-Path $appDir 'ImagePress.runtimeconfig.json') | ConvertFrom-Json).runtimeOptions.includedFrameworks
foreach ($framework in $runtime) {
    $runtimePackage = Join-Path $packages ($framework.name.ToLowerInvariant() + '.runtime.win-x64\' + $framework.version)
    Get-ChildItem -LiteralPath $runtimePackage -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stage ($framework.name + '-' + $_.Name)) }
}

$msix = Join-Path $dist ("$projectName-$Version-x64.msix")
& (Join-Path $tools 'makeappx.exe') pack /d $stage /p $msix /o | Out-Null
Assert-Native 'MSIX packaging'
$subject = $xml.Package.Identity.Publisher
if ($CertificateThumbprint) { $cert = Get-Item -LiteralPath ("Cert:\CurrentUser\My\" + $CertificateThumbprint) }
else {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object {
        $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(14)
    } | Sort-Object NotAfter -Descending | Select-Object -First 1
    if (!$cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -FriendlyName 'ImagePress package signing' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(3)
    }
}
if (!$cert.HasPrivateKey -or $cert.Subject -ne $subject) { throw 'The signing certificate must match the manifest publisher.' }
& (Join-Path $tools 'signtool.exe') sign /fd SHA256 /s My /sha1 $cert.Thumbprint $msix | Out-Null
Assert-Native 'MSIX signing'
$publicCert = Join-Path $dist "$projectName.cer"
Export-Certificate -Cert $cert -FilePath $publicCert -Force | Out-Null
$instructions = Join-Path $dist '安装说明.txt'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '安装说明.txt') -Destination $instructions -Force
$bundle = Join-Path $dist "$projectName-win-x64.zip"
Compress-Archive -LiteralPath @($msix, $publicCert, $instructions) -DestinationPath $bundle -CompressionLevel NoCompression -Force
[IO.File]::WriteAllText((Join-Path $workDir 'last-layout.txt'),$stage)
Write-Host "MSIX: $msix"
Write-Host "Distribution ZIP: $bundle"
Write-Host "Publisher certificate: $($cert.Thumbprint)"
