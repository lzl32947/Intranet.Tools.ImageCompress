param([version]$Version, [string]$CertificateThumbprint, [string]$Compiler)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Version) {
    $props = [xml](Get-Content -Raw -LiteralPath (Join-Path $root 'Directory.Build.props'))
    $Version = [version]($props.Project.PropertyGroup.Version + '.0')
}
if ($Version.Revision -lt 0) { throw 'Use a four-part package version, for example 1.0.1.0.' }
$appVersion = $Version.ToString(3)
$work = Join-Path $root 'artifacts\exe'
$compile = Join-Path $work 'compile'
$dist = Join-Path $work $Version.ToString()
$stage = Join-Path $work ('stage-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $stage 'app'
$menu = Join-Path $app 'Menu'
$identity = Join-Path $stage 'identity'
foreach ($dir in @($dist, $menu, $identity)) { [void][IO.Directory]::CreateDirectory($dir) }
function Assert-Native([string]$step) { if ($LASTEXITCODE -ne 0) { throw "$step failed ($LASTEXITCODE)" } }

if (!$Compiler) {
    $Compiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    if (!$Compiler) {
        $dirs = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, (Join-Path $env:LOCALAPPDATA 'Programs'))
        $Compiler = $dirs | ForEach-Object { Join-Path $_ 'Inno Setup 7\ISCC.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'Inno Setup 7.x is required. Specify -Compiler with the full path to ISCC.exe.' }
$kit = Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' KitsRoot10
$sdk = Get-ChildItem -LiteralPath (Join-Path $kit 'bin') -Directory | Where-Object {
    $_.Name -match '^\d+\.' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'x64\makeappx.exe'))
} | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$sdk) { throw 'Windows SDK x64 packaging tools are required.' }
$tools = Join-Path $sdk.FullName 'x64'
$signTool = Join-Path $tools 'signtool.exe'
$manifest = [xml][IO.File]::ReadAllText((Join-Path $PSScriptRoot 'AppxManifest.xml'))
$manifest.Package.Identity.Version = $Version.ToString()
$publisher = $manifest.Package.Identity.Publisher
if ($CertificateThumbprint) { $cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint" }
else {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object {
        $_.Subject -eq $publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(14)
    } | Sort-Object NotAfter -Descending | Select-Object -First 1
    if (!$cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $publisher -FriendlyName 'ImagePress package signing' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(3)
    }
}
if (!$cert.HasPrivateKey -or $cert.Subject -ne $publisher) { throw 'The signing certificate must match the manifest publisher.' }
function Sign-File([string]$path) {
    & $signTool sign /fd SHA256 /s My /sha1 $cert.Thumbprint $path | Out-Null
    Assert-Native "Sign $path"
}

$common = @('-c','Release','-r','win-x64','--artifacts-path',$compile,"-p:Version=$appVersion",'-p:ContinuousIntegrationBuild=true','-p:DebugType=None')
& dotnet publish (Join-Path $root 'src\Desktop\Desktop.csproj') @common --self-contained false -p:DebugSymbols=false -o $app
Assert-Native 'Framework-dependent WPF publish'
$runtime = (Get-Content -Raw -LiteralPath (Join-Path $app 'ImagePress.runtimeconfig.json') | ConvertFrom-Json).runtimeOptions
if ($runtime.includedFrameworks -or 'Microsoft.WindowsDesktop.App' -notin $runtime.frameworks.name) { throw 'The desktop EXE must use the installed Windows Desktop Runtime.' }
foreach ($name in @('coreclr.dll','hostfxr.dll','hostpolicy.dll','clrjit.dll','System.Private.CoreLib.dll','PresentationFramework.dll')) {
    if (Test-Path -LiteralPath (Join-Path $app $name)) { throw "Unexpected bundled framework file: $name" }
}
$shellOut = Join-Path $stage 'shell'
& dotnet publish (Join-Path $root 'src\Shell\Shell.csproj') @common -p:NativeDebugSymbols=false -o $shellOut
Assert-Native 'Native shell extension publish'
Copy-Item -LiteralPath (Join-Path $shellOut 'ImagePressShell.dll') -Destination $app
Sign-File (Join-Path $app 'ImagePress.exe')
Sign-File (Join-Path $app 'ImagePressShell.dll')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets') -Destination $app -Recurse
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $app 'LICENSE.txt')
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages' }
$deps = Get-Content -Raw -LiteralPath (Join-Path $app 'ImagePress.deps.json') | ConvertFrom-Json
$magick = $deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'Magick.NET-Q16-x64/*' }
Copy-Item -LiteralPath (Join-Path $packages ($magick.ToLowerInvariant() + '/Notice.txt')) -Destination (Join-Path $app 'Magick.NET-NOTICE.txt')
$dotnet = Split-Path (Get-Command dotnet.exe).Source
foreach ($notice in @('LICENSE.txt','ThirdPartyNotices.txt')) {
    Copy-Item -LiteralPath (Join-Path $dotnet $notice) -Destination (Join-Path $app "dotnet-$notice")
}

$manifestPath = Join-Path $identity 'AppxManifest.xml'
$manifest.Save($manifestPath)
Copy-Item -LiteralPath $manifestPath -Destination $menu
$identityPackage = Join-Path $menu 'ImagePress.Menu.msix'
& (Join-Path $tools 'makeappx.exe') pack /o /nv /d $identity /p $identityPackage | Out-Null
Assert-Native 'Menu identity packaging'
Sign-File $identityPackage
$publicCert = Join-Path $menu 'Publisher.cer'
Export-Certificate -Cert $cert -FilePath $publicCert | Out-Null
& (Join-Path $root 'tests\Verify-Package.ps1') -Package $identityPackage -Certificate $publicCert
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Menu.ps1') -Destination $menu
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '安装说明.txt') -Destination $app

# The privileged helper gets the exact release certificate in memory, not a path under the user's control.
$trustScript = @'
$ErrorActionPreference = 'Stop'
$publicCert = [Security.Cryptography.X509Certificates.X509Certificate2]::new([Convert]::FromBase64String('__CERT__'))
$store = [Security.Cryptography.X509Certificates.X509Store]::new('TrustedPeople','LocalMachine')
try { $store.Open('ReadWrite'); $store.Add($publicCert) } finally { $store.Close(); $publicCert.Dispose() }
'@
$trustScript = $trustScript.Replace('__CERT__', [Convert]::ToBase64String($cert.RawData))
$trustCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($trustScript))
$sign = '/Spublisher=$q' + $signTool + '$q sign /fd SHA256 /s My /sha1 ' + $cert.Thumbprint + ' $f'
& $Compiler '/Q' "/O$dist" "/DLayout=$app" "/DAppVersion=$appVersion" "/DPackageVersion=$Version" "/DTrustCommand=$trustCommand" $sign (Join-Path $PSScriptRoot 'Setup.iss')
Assert-Native 'EXE installer compilation'
$setup = Get-Item -LiteralPath (Join-Path $dist 'Intranet.Tools.ImageCompress-Setup-x64.exe')
$hash = (Get-FileHash -LiteralPath $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'), "$hash  $($setup.Name)`n", [Text.UTF8Encoding]::new($false))
Write-Host "$($setup.FullName) ($($setup.Length) bytes)"
