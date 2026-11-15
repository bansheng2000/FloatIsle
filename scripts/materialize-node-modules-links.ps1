# Turns the pnpm junctions inside node_modules into real directories.
#
# On this machine every junction under node_modules answers fs.stat with
# "UNKNOWN: unknown error", which makes tsc miss @types/react and the Tauri CLI miss its native
# binding. A real directory with the target's contents in the same place fixes it. The script
# never touches anything outside node_modules, and it is safe to run when nothing is left to do.
#
#   powershell -File scripts/materialize-node-modules-links.ps1            # dry run, writes the report
#   powershell -File scripts/materialize-node-modules-links.ps1 --apply    # materializes, rolls back on failure
param(
  [switch]$Apply,
  [switch]$DryRun,
  [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest = @()
)

$ErrorActionPreference = 'Stop'
foreach ($flag in @($Rest)) {
  if ([string]::IsNullOrWhiteSpace($flag)) { continue }
  $name = $flag.TrimStart('-').ToLowerInvariant()
  if ($name -eq 'apply') { $Apply = $true }
  elseif ($name -eq 'dry-run' -or $name -eq 'dryrun') { $DryRun = $true }
  else { Write-Output "unknown flag: $flag"; exit 2 }
}
if ($Apply -and $DryRun) { Write-Output '--apply and --dry-run cannot be combined'; exit 2 }
$mode = if ($Apply) { 'apply' } else { 'dry-run' }

$repoRoot = Split-Path -Parent $PSScriptRoot
$modules = Join-Path $repoRoot 'node_modules'
$reportPath = Join-Path $repoRoot 'logs\materialize-node-modules-links.log'
$reportDir = Split-Path -Parent $reportPath
if (-not (Test-Path -LiteralPath $reportDir)) { New-Item -ItemType Directory -Path $reportDir | Out-Null }
if (-not (Test-Path -LiteralPath $modules)) { Write-Output "no node_modules at $modules"; exit 2 }

$report = New-Object System.Collections.Generic.List[string]
function Write-Report([string]$line) { $report.Add($line); Write-Output $line }

Write-Report '== node_modules junction materialization =='
Write-Report ("time:   " + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))
Write-Report ("mode:   " + $mode)
Write-Report ("root:   " + $modules)
Write-Report ''

# Walk node_modules without descending into reparse points: a junction may point anywhere.
$links = New-Object System.Collections.Generic.List[object]
$realEntries = New-Object System.Collections.Generic.List[string]
$queue = New-Object System.Collections.Generic.Queue[string]
$queue.Enqueue($modules)
while ($queue.Count -gt 0) {
  $dir = $queue.Dequeue()
  try { $children = @(Get-ChildItem -LiteralPath $dir -Force -ErrorAction Stop) }
  catch {
    Write-Report ("unreadable directory, skipped: " + $dir)
    continue
  }
  foreach ($child in $children) {
    $isLink = [bool]($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint)
    if ($isLink) {
      $links.Add($child)
      continue
    }
    if ($child.PSIsContainer) { $queue.Enqueue($child.FullName) }
    elseif ($dir -eq $modules) { $realEntries.Add($child.Name) }
  }
}

$before = $links.Count
Write-Report ("junctions found: " + $before)
Write-Report ''

function Get-LinkTarget($link) {
  $target = $null
  try { $target = @($link.Target)[0] } catch { $target = $null }
  if (-not $target) { return $null }
  if (-not [System.IO.Path]::IsPathRooted($target)) {
    $target = [System.IO.Path]::GetFullPath((Join-Path $link.DirectoryName $target))
  }
  return $target
}

function Test-Readable([string]$path) {
  try {
    $probe = @(Get-ChildItem -LiteralPath $path -Force -ErrorAction Stop)
    return $true
  } catch { return $false }
}

function Restore-Junction([string]$path, [string]$target) {
  & cmd.exe /c mklink /J $path $target | Out-Null
  return (Test-Path -LiteralPath $path)
}

function Copy-Tree([string]$source, [string]$destination) {
  & robocopy $source $destination /E /COPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
  return ($LASTEXITCODE -lt 8)
}

function Count-Files([string]$path) {
  try { return @(Get-ChildItem -LiteralPath $path -Recurse -File -Force -ErrorAction Stop).Count }
  catch { return -1 }
}

$materialized = 0
$skipped = 0
$failed = 0
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

foreach ($link in $links) {
  $target = Get-LinkTarget $link
  $readable = if ($target) { Test-Readable $target } else { $false }
  $kind = if ($link.PSIsContainer) { 'junction' } else { 'file-link' }
  if ($kind -eq 'file-link') {
    $skipped++
    Write-Report ("skip (file reparse point): " + $link.FullName)
    continue
  }
  if (-not $target -or -not $readable) {
    $skipped++
    Write-Report ("skip (target unreadable): " + $link.FullName + " -> " + ($(if ($target) { $target } else { '<none>' })))
    continue
  }
  if ($mode -eq 'dry-run') {
    Write-Report ("would materialize: " + $link.FullName + " -> " + $target)
    continue
  }

  $staging = $link.FullName + '.__fs_staging'
  if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
  if (-not (Copy-Tree $target $staging)) {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }
    $failed++
    Write-Report ("FAILED copy, link untouched: " + $link.FullName)
    continue
  }
  $sourceFiles = Count-Files $target
  $stagedFiles = Count-Files $staging
  if ($sourceFiles -lt 0 -or $stagedFiles -ne $sourceFiles) {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    $failed++
    Write-Report ("FAILED verify ($stagedFiles != $sourceFiles files), link untouched: " + $link.FullName)
    continue
  }

  & cmd.exe /c rmdir $link.FullName | Out-Null
  if (Test-Path -LiteralPath $link.FullName) {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    $failed++
    Write-Report ("FAILED unlink, link left as it was: " + $link.FullName)
    continue
  }
  try {
    Move-Item -LiteralPath $staging -Destination $link.FullName -ErrorAction Stop
  } catch {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    if (Restore-Junction $link.FullName $target) {
      $failed++
      Write-Report ("FAILED move, junction restored: " + $link.FullName)
    } else {
      $failed++
      Write-Report ("FAILED move and restore, needs attention: " + $link.FullName)
    }
    continue
  }
  $materialized++
  Write-Report ("materialized: " + $link.FullName)
}

$stopwatch.Stop()
Write-Report ''
Write-Report ("已实体化 " + $materialized + " 个 / 跳过 " + $skipped + " 个 / 失败 " + $failed + " 个")
Write-Report ("junctions before: " + $before)
Write-Report ("elapsed: " + [math]::Round($stopwatch.Elapsed.TotalSeconds, 1) + "s")
if ($realEntries.Count -gt 0) {
  $listed = $realEntries | Select-Object -First 200
  Write-Report ("real entries left alone: " + $realEntries.Count)
  foreach ($entry in $listed) { Write-Report ("  untouched: " + $entry) }
  if ($realEntries.Count -gt $listed.Count) {
    Write-Report ("  ... " + ($realEntries.Count - $listed.Count) + " more not listed")
  }
}

Set-Content -LiteralPath $reportPath -Value ($report -join [Environment]::NewLine) -Encoding UTF8
Write-Output ("report: " + $reportPath)
if ($failed -gt 0) { exit 1 }
exit 0
