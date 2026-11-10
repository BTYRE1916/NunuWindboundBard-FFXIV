param(
    [Parameter(Mandatory = $true)]
    [string] $RepoPath
)
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'Apply-WindboundStagePatch.py'
$py = Get-Command py -ErrorAction SilentlyContinue
if ($null -ne $py) {
    & py -3 $script $RepoPath
} else {
    & python $script $RepoPath
}
if ($LASTEXITCODE -ne 0) { throw "Patch failed; source not changed if preflight rejected it." }
