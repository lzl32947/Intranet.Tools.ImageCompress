param(
    [Parameter(Mandatory)][ValidateSet('Prepare','Register','Remove')][string]$Action,
    [Parameter(Mandatory)][string]$AppDirectory,
    [Parameter(Mandatory)][version]$Version,
    [string]$Certificate,
    [string]$ErrorLog
)
$ErrorActionPreference = 'Stop'
$family = 'ImagePress.Desktop_xnxz7mp0jqdbm'
$root = [IO.Path]::GetFullPath($AppDirectory).TrimEnd('\')
$menu = Join-Path $root 'Menu'
if (!$Certificate) { $Certificate = Join-Path $menu 'Publisher.cer' }

function Test-PublisherTrust {
    $cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($Certificate)
    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new($true)
    try {
        if (Test-Path -LiteralPath ('Cert:\LocalMachine\TrustedPeople\' + $cert.Thumbprint)) { return $true }
        $chain.ChainPolicy.RevocationMode = 'NoCheck'
        return $chain.Build($cert)
    } finally { $chain.Dispose(); $cert.Dispose() }
}

function Get-ExternalPath($package) {
    $manager = [Windows.Management.Deployment.PackageManager,Windows.Management.Deployment,ContentType=WindowsRuntime]::new()
    $entry = $manager.FindPackageForUser('', $package.PackageFullName)
    if ($entry.EffectiveExternalLocation) { return $entry.EffectiveExternalLocation.Path.TrimEnd('\') }
}

try {
    $package = Get-AppxPackage -Name 'ImagePress.Desktop' | Where-Object PackageFamilyName -eq $family
    if ($Action -eq 'Remove') {
        if ($package -and [version]$package.Version -eq $Version -and (Get-ExternalPath $package) -eq $root) {
            Remove-AppxPackage -Package $package.PackageFullName
        }
        exit 0
    }

    if ($package -and [version]$package.Version -gt $Version) { throw 'A newer version is installed.' }
    if ($package -and [version]$package.Version -eq $Version) {
        $previousPath = Get-ExternalPath $package
        if ($previousPath -and $previousPath -ne $root) { throw "Use the existing installation directory: $previousPath" }
    }
    $trusted = Test-PublisherTrust
    $developerMode = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense -eq 1
    if (!$trusted -and !$developerMode) { exit 10 }
    if ($Action -eq 'Prepare') { exit 0 }

    $local = [Environment]::GetFolderPath('LocalApplicationData')
    $oldSettings = Join-Path $local "Packages\$family\LocalState\last-run.txt"
    $newSettings = Join-Path $local 'Intranet.Tools.ImageCompress\last-run.txt'
    if ([IO.File]::Exists($oldSettings) -and ![IO.File]::Exists($newSettings)) {
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($newSettings))
        try { [IO.File]::Copy($oldSettings, $newSettings, $false) }
        catch [IO.IOException] { if (![IO.File]::Exists($newSettings)) { throw } }
    }

    if ($trusted) {
        Add-AppxPackage -Path (Join-Path $menu 'ImagePress.Menu.msix') -ExternalLocation $root
    } else {
        # Only when the machine already permits developer registration.
        Add-AppxPackage -Register (Join-Path $menu 'AppxManifest.xml') -ExternalLocation $root
    }
    $installed = Get-AppxPackage -Name 'ImagePress.Desktop' | Where-Object PackageFamilyName -eq $family
    if (!$installed -or [version]$installed.Version -ne $Version -or (Get-ExternalPath $installed) -ne $root) {
        throw 'Windows did not register the menu at the installed EXE location.'
    }
    exit 0
} catch {
    if ($ErrorLog) { [IO.File]::WriteAllText($ErrorLog, $_.Exception.Message, [Text.UTF8Encoding]::new($true)) }
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
