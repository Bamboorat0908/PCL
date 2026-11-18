param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Revision,
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [string]$Server = 'root@45.207.197.246',
    [int]$Port = 64886
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$manifestHash = (Get-FileHash -LiteralPath (Join-Path $ReleaseDirectory 'latest.json')).Hash.ToLowerInvariant()
$signatureHash = (Get-FileHash -LiteralPath (Join-Path $ReleaseDirectory 'latest.sig')).Hash.ToLowerInvariant()
$upload = '/tmp/brcl-transfer-' + [Guid]::NewGuid().ToString('N') + '.py'
$transport = @('-o','BatchMode=yes','-o','ConnectTimeout=10','-o','ServerAliveInterval=10','-o','ServerAliveCountMax=3')
try {
    & scp @transport -P $Port (Join-Path $repo 'website\brcl\deploy\sync-transfer.py') "$($Server):$upload"
    if ($LASTEXITCODE -ne 0) { throw 'HTTPS synchronization helper upload failed.' }
    & ssh @transport -p $Port $Server "flock -x -w 15 /var/lock/brcl-transfer.lock python3 $upload --revision $Revision --manifest-sha256 $manifestHash --signature-sha256 $signatureHash"
    if ($LASTEXITCODE -ne 0) { throw 'Primary HTTPS synchronization failed.' }
} finally {
    & ssh @transport -p $Port $Server "rm -f $upload"
}
