<#
.SYNOPSIS
    Builds the MatSplit container image and (re)deploys the development stack.

.DESCRIPTION
    The image version is "local-<yyyyMMdd>". Afterwards the dev stack is
    rebuilt and restarted via docker-compose.dev.yml:
        app            -> http://localhost:4774
        sqlite browser -> http://localhost:4775

.EXAMPLE
    ./scripts/build.ps1

.EXAMPLE
    ./scripts/build.ps1 -NoCache -Follow

.EXAMPLE
    ./scripts/build.ps1 -BuildOnly -Configuration Release
#>
[CmdletBinding()]
param(
    # Build configuration passed to "dotnet publish" inside the image.
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # Build the image without using the layer cache.
    [switch]$NoCache,

    # Build the image but leave the running stack untouched.
    [switch]$BuildOnly,

    # Follow the application log after the deployment.
    [switch]$Follow
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$global:LASTEXITCODE = 0

$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFileName = 'docker-compose.dev.yml'
$composeFile = Join-Path $repoRoot $composeFileName
$version = 'local-' + (Get-Date).ToString('yyyyMMdd')
$buildDate = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

function Invoke-Docker {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    Write-Host ''
    Write-Host "==> $Title" -ForegroundColor Cyan
    Write-Host "    docker $($Arguments -join ' ')" -ForegroundColor DarkGray

    & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Title failed (exit code $LASTEXITCODE)."
    }
}

function Get-GitRevision {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        return 'unknown'
    }

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $revision = & git -C $Path rev-parse --short HEAD 2>$null | Select-Object -First 1
        if ($LASTEXITCODE -eq 0 -and $revision) {
            return $revision.ToString().Trim()
        }
        return 'unknown'
    }
    catch {
        return 'unknown'
    }
    finally {
        $ErrorActionPreference = $previous
        $global:LASTEXITCODE = 0
    }
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'docker was not found in PATH.'
}
if (-not (Test-Path -LiteralPath $composeFile)) {
    throw "Missing compose file: $composeFile"
}

Write-Host 'MatSplit build' -ForegroundColor Yellow
Write-Host "  repo          : $repoRoot"
Write-Host "  version       : $version"
Write-Host "  configuration : $Configuration"

Push-Location $repoRoot
try {
    $env:MATSPLIT_VERSION = $version

    $buildArgs = @(
        'build',
        '--file', 'Dockerfile',
        '--tag', "matsplit:$version",
        '--tag', 'matsplit:local',
        '--build-arg', "BUILD_CONFIGURATION=$Configuration",
        '--build-arg', "APP_VERSION=$version",
        '--build-arg', "BUILD_DATE=$buildDate",
        '--build-arg', "VCS_REF=$(Get-GitRevision -Path $repoRoot)"
    )
    if ($NoCache) {
        $buildArgs += '--no-cache'
    }
    $buildArgs += '.'

    Invoke-Docker -Title "Build image matsplit:$version" -Arguments $buildArgs

    if ($BuildOnly) {
        Write-Host ''
        Write-Host "Image matsplit:$version built. Stack untouched (-BuildOnly)." -ForegroundColor Green
        return
    }

    Invoke-Docker -Title 'Deploy dev stack' -Arguments @(
        'compose', '-f', $composeFileName, 'up', '-d', '--build', '--remove-orphans')

    # keep the version tag pointing at the image that is actually running
    & docker image tag 'matsplit:dev' "matsplit:$version" | Out-Null
    $global:LASTEXITCODE = 0

    Invoke-Docker -Title 'Stack status' -Arguments @('compose', '-f', $composeFileName, 'ps')

    Write-Host ''
    Write-Host 'MatSplit    -> http://localhost:4774' -ForegroundColor Green
    Write-Host 'SQLite view -> http://localhost:4775' -ForegroundColor Green
    Write-Host "Logs        -> docker compose -f $composeFileName logs -f msbi" -ForegroundColor DarkGray

    if ($Follow) {
        & docker compose -f $composeFileName logs -f msbi
    }
}
finally {
    Remove-Item Env:MATSPLIT_VERSION -ErrorAction SilentlyContinue
    Pop-Location
}
