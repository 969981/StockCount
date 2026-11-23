#requires -Version 7.0
<#
.SYNOPSIS
  Dispatch the multi-platform StockCount GitHub prerelease workflow.
.DESCRIPTION
  Validates the requested version, repository, all four signing secret NAMES,
  workflow availability and whether the version tag already exists.
  Triggers the main-branch workflow to build Android Debug and Release APK,
  Windows Setup and Portable, and publishes only when all checks pass.
  A successful dispatch is NOT proof that the Release was published.
.EXAMPLE
  pwsh -File .\scripts\publish-stockcount-release.ps1 -Version 0.2.0-preview.3
.EXAMPLE
  pwsh -File .\scripts\publish-stockcount-release.ps1 -Version 0.2.0-preview.3 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
  [Parameter(Mandatory)]
  [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9]+(\.[A-Za-z0-9]+)*)?$')]
  [string]$Version,
  [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
  [string]$Repository = '969981/StockCount'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workflow = 'publish-stockcount.yml'
$tag = "v$Version"
$releaseUrl = "https://github.com/$Repository/releases/tag/$tag"
$actionsUrl = "https://github.com/$Repository/actions/workflows/$workflow"
Write-Host "Repository: $Repository"
Write-Host "Release tag: $tag"
Write-Host 'Required assets: Android Debug-Signed.apk, Android Release-Signed.apk, Windows Setup.exe, Windows Portable.zip and SHA256SUMS.txt.'

if (-not $PSCmdlet.ShouldProcess("$Repository $tag", "Trigger main-branch GitHub release workflow")) {
  Write-Host 'DRY RUN: no GitHub workflow was dispatched and no Release was created.'
  return
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
  throw 'GitHub CLI (gh) is required; install it and run gh auth login.'
}
& gh auth status
if ($LASTEXITCODE -ne 0) { throw 'gh is not authenticated; run gh auth login.' }

& gh repo view $Repository --json nameWithOwner --jq '.nameWithOwner'
if ($LASTEXITCODE -ne 0) { throw "Cannot access repository $Repository." }

$requiredSecrets = @(
  'STOCKCOUNT_ANDROID_KEYSTORE_BASE64',
  'STOCKCOUNT_ANDROID_KEY_ALIAS',
  'STOCKCOUNT_ANDROID_STORE_PASS',
  'STOCKCOUNT_ANDROID_KEY_PASS'
)
$json = & gh secret list --repo $Repository --json name
if ($LASTEXITCODE -ne 0) { throw 'Cannot list signing secret names in the repository.' }
$names = @($json | ConvertFrom-Json | ForEach-Object { $_.name })
$missing = @($requiredSecrets | Where-Object { $_ -notin $names })
if ($missing.Count -gt 0) {
  throw "Missing Android signing Secrets: $($missing -join ', '). Run scripts/configure-android-release-secrets.ps1 first."
}

& gh workflow view $workflow --repo $Repository
if ($LASTEXITCODE -ne 0) {
  throw "Release workflow $workflow is unavailable on GitHub main."
}
# Protect immutable tags. An existing tag (even without a GitHub Release)
# should never be reused for a different set of binaries.
& gh api "repos/$Repository/git/ref/tags/$tag" --jq '.ref' 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
  throw "Tag $tag already exists. Choose a new version; do not overwrite an existing signing/distribution identity."
}
& gh release view $tag --repo $Repository --json tagName 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
  throw "Release $tag already exists. Choose a new version."
}

Write-Host "Dispatching $workflow from main with version=$Version ..." -ForegroundColor Cyan
& gh workflow run $workflow --repo $Repository --ref main --field "version=$Version"
if ($LASTEXITCODE -ne 0) {
  throw 'GitHub workflow dispatch failed. No release success is implied.'
}
Write-Host 'Workflow dispatch accepted. The Release will only appear if both platform builds, signing checks and asset validation SUCCEED.' -ForegroundColor Green
Write-Host "Actions: $actionsUrl"
Write-Host "Expected Release page after successful completion: $releaseUrl"
Write-Host 'Check recent runs:'
Write-Host ("gh run list --repo " + $Repository + " --workflow " + $workflow + " --limit 5")
Write-Host 'After completion verify all four assets and SHA256SUMS; do not claim release success from dispatch alone.'
