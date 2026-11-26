param([Parameter(Mandatory)][string]$Package, [Parameter(Mandatory)][string]$Certificate)
# Verify the signed digest and the block map without requiring machine-wide trust.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security.Cryptography.Pkcs
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Package).Path)
$pub = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $Certificate).Path)
function Read-Part([string]$name) {
    $entry = $zip.GetEntry($name)
    if (!$entry) { throw "Missing package part: $name" }
    $inputStream = $entry.Open()
    $buffer = [IO.MemoryStream]::new()
    try { $inputStream.CopyTo($buffer); return ,$buffer.ToArray() } finally { $inputStream.Dispose(); $buffer.Dispose() }
}
$sha = [Security.Cryptography.SHA256]::Create()
try {
    [byte[]]$raw = Read-Part 'AppxSignature.p7x'
    if ([Text.Encoding]::ASCII.GetString($raw,0,4) -ne 'PKCX') { throw 'Invalid signature envelope.' }
    $cms = [Security.Cryptography.Pkcs.SignedCms]::new()
    $cms.Decode([byte[]]($raw[4..($raw.Length-1)]))
    $cms.CheckSignature($true)
    if ($cms.SignerInfos.Count -ne 1 -or $cms.SignerInfos[0].Certificate.Thumbprint -ne $pub.Thumbprint -or $pub.HasPrivateKey) { throw 'Release signer mismatch.' }
    $signed = [Convert]::ToHexString($cms.ContentInfo.Content)
    [byte[]]$map = Read-Part 'AppxBlockMap.xml'
    $mapHash = [Convert]::ToHexString($sha.ComputeHash($map))
    if (!$signed.Contains('4158424D' + $mapHash)) { throw 'The block map differs from the signed AXBM digest.' }
    [xml]$manifest = [Text.Encoding]::UTF8.GetString((Read-Part 'AppxManifest.xml')).TrimStart([char]0xFEFF)
    if ($manifest.Package.Identity.Publisher -ne $pub.Subject) { throw 'Manifest publisher mismatch.' }
    [xml]$mapXml = [Text.Encoding]::UTF8.GetString($map).TrimStart([char]0xFEFF)
    $buffer = [byte[]]::new(65536)
    $count = 0
    foreach ($file in $mapXml.BlockMap.File) {
        $part = $zip.GetEntry(([Uri]::UnescapeDataString($file.Name)).Replace('\','/'))
        if (!$part -or $part.Length -ne [long]$file.Size) { throw "File size mismatch: $($file.Name)" }
        $s = $part.Open()
        try {
            $remaining = $part.Length
            foreach ($block in $file.SelectNodes('*[local-name()="Block"]')) {
                $n = [int][Math]::Min([long]65536,$remaining)
                $s.ReadExactly($buffer,0,$n)
                if ([Convert]::ToBase64String($sha.ComputeHash($buffer,0,$n)) -cne $block.Hash) { throw "Block hash mismatch: $($file.Name)" }
                $remaining -= $n
            }
            if ($remaining -ne 0) { throw "Incomplete block map: $($file.Name)" }
            $count++
        } finally { $s.Dispose() }
    }
    Write-Host "Verified $count payload files, signed block map and signer $($pub.Thumbprint)."
} finally { $zip.Dispose(); $pub.Dispose(); $sha.Dispose() }
