# validate-resx.ps1
# Validates that all satellite RESX files have the same keys as the base Strings.resx
# and that placeholder patterns ({0}, {1}, etc.) match between base and translations.
#
# Usage: pwsh -File Scripts/validate-resx.ps1 [-Strict]
# Exit codes: 0 = OK, 1 = errors found

param(
    [switch]$Strict  # Treat warnings (extra keys) as errors
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$resourceDir = Join-Path (Split-Path -Parent $scriptDir) 'Resources'
$basePath = Join-Path $resourceDir 'Strings.resx'

if (-not (Test-Path $basePath)) {
    Write-Error "Base RESX not found: $basePath"
    exit 1
}

# Parse RESX file and return hashtable of key -> value
function Get-ResxEntries {
    param([string]$Path)
    $entries = @{}
    [xml]$xml = Get-Content $Path -Encoding UTF8
    foreach ($data in $xml.root.data) {
        $entries[$data.name] = $data.value
    }
    return $entries
}

# Extract placeholders like {0}, {1} from a string
function Get-Placeholders {
    param([string]$Text)
    $matches = [regex]::Matches($Text, '\{(\d+)\}')
    return ($matches | ForEach-Object { $_.Value } | Sort-Object -Unique)
}

$baseEntries = Get-ResxEntries $basePath
$baseKeys = $baseEntries.Keys | Sort-Object

Write-Host "Base RESX: $($baseKeys.Count) keys" -ForegroundColor Cyan
Write-Host ""

$satelliteFiles = Get-ChildItem $resourceDir -Filter 'Strings.*.resx' | Sort-Object Name
$totalErrors = 0
$totalWarnings = 0

foreach ($file in $satelliteFiles) {
    $lang = $file.Name -replace '^Strings\.(.+)\.resx$','$1'
    Write-Host "Checking: $($file.Name) ($lang)" -ForegroundColor Yellow

    $satEntries = Get-ResxEntries $file.FullName
    $satKeys = $satEntries.Keys | Sort-Object
    $errors = 0
    $warnings = 0

    # Check for missing keys
    $missing = $baseKeys | Where-Object { $_ -notin $satKeys }
    foreach ($key in $missing) {
        Write-Host "  MISSING: $key" -ForegroundColor Red
        $errors++
    }

    # Check for extra keys (not in base)
    $extra = $satKeys | Where-Object { $_ -notin $baseKeys }
    foreach ($key in $extra) {
        Write-Host "  EXTRA:   $key" -ForegroundColor DarkYellow
        $warnings++
    }

    # Check placeholder consistency
    foreach ($key in $baseKeys) {
        if ($key -notin $satKeys) { continue }
        $basePlaceholders = Get-Placeholders $baseEntries[$key]
        $satPlaceholders = Get-Placeholders $satEntries[$key]

        $basePh = ($basePlaceholders -join ',')
        $satPh = ($satPlaceholders -join ',')

        if ($basePh -ne $satPh) {
            Write-Host "  PLACEHOLDER MISMATCH: $key  base=[$basePh]  $lang=[$satPh]" -ForegroundColor Red
            $errors++
        }
    }

    # Check for empty values where base has content
    foreach ($key in $baseKeys) {
        if ($key -notin $satKeys) { continue }
        if (-not [string]::IsNullOrWhiteSpace($baseEntries[$key]) -and
            [string]::IsNullOrWhiteSpace($satEntries[$key])) {
            Write-Host "  EMPTY:   $key (base has content, translation is empty)" -ForegroundColor Red
            $errors++
        }
    }

    if ($errors -eq 0 -and $warnings -eq 0) {
        Write-Host "  OK ($($satKeys.Count) keys)" -ForegroundColor Green
    } else {
        if ($errors -gt 0) {
            Write-Host "  $errors error(s), $warnings warning(s)" -ForegroundColor Red
        } else {
            Write-Host "  $warnings warning(s)" -ForegroundColor DarkYellow
        }
    }

    $totalErrors += $errors
    if ($Strict) { $totalErrors += $warnings }
    else { $totalWarnings += $warnings }

    Write-Host ""
}

Write-Host "────────────────────────────────────────" -ForegroundColor Cyan
if ($totalErrors -gt 0) {
    Write-Host "FAILED: $totalErrors error(s), $totalWarnings warning(s)" -ForegroundColor Red
    exit 1
} elseif ($totalWarnings -gt 0) {
    Write-Host "PASSED with $totalWarnings warning(s)" -ForegroundColor DarkYellow
    exit 0
} else {
    Write-Host "PASSED: All translations are in sync" -ForegroundColor Green
    exit 0
}
