<#
.SYNOPSIS
    Builds ClaimSeeker and runs every offline check. Run it after every change, before calling work done.

.DESCRIPTION
    Nothing here contacts a real source. The harnesses use invented records and a page served from this
    machine; the engine smoke replays recorded answers. The only step that uses the network is the
    optional -IncludeAudit, which asks NuGet about known vulnerabilities.

    Written for PowerShell 7 (pwsh), which is what CI runs. It avoids syntax that Windows PowerShell
    5.1 lacks, but it has only ever been run under pwsh. Keep this file ASCII.

.PARAMETER IncludeAudit
    Also fail if any package, direct or transitive, has a known vulnerability. Needs the network.
#>
param(
    [string] $Configuration = "Release",
    [switch] $IncludeAudit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

# ---------------------------------------------------------------------------------------------
# The one place the offline total is pinned.
#
# Adding or removing a check in any harness changes this number, and the harness tables in
# README.md and docs/ClaimSeeker-Project-Summary.md, in the same change. The run fails if the
# measured sum differs, so a check that quietly stops running is a hard failure and not a
# smaller number nobody reads.
#
#   CoreHarness 151 + ScoutHarness 158 + ScorerHarness 90 + StoreParityHarness 121 + GateHarness 93
#   + DispatcherNoSendHarness 51 + LifecycleHarness 144 + EngineHarness 128 + PortalHarness 45 = 981
# ---------------------------------------------------------------------------------------------
$ExpectedOfflineTotal = 981

$Harnesses = @(
    "CoreHarness",
    "ScoutHarness",
    "ScorerHarness",
    "StoreParityHarness",
    "GateHarness",
    "DispatcherNoSendHarness",
    "LifecycleHarness",
    "EngineHarness",
    "PortalHarness"
)

# Docs that carry the per-harness table. Each row must equal what was just measured.
$HarnessTableDocs = @(
    "README.md",
    "docs/ClaimSeeker-Project-Summary.md"
)

# Wording the docs and the code agree on. When the fact changes, the doc and this list change together.
$RequiredPhrases = @(
    @{ File = "README.md";                            Phrase = "has never been run against a real source" },
    @{ File = "docs/ClaimSeeker-Project-Summary.md";  Phrase = "has never been run against a real source" },
    @{ File = "docs/SPECIFICATION.md";                Phrase = "has never been run against a real source" },
    @{ File = "playbooks/missingmoney.md";            Phrase = "Not yet re-run by this codebase" },
    @{ File = "README.md";                            Phrase = "Filing a claim is free" },
    @{ File = "CLAUDE.md";                            Phrase = "scripts/Verify.ps1" },
    @{ File = "AGENTS.md";                            Phrase = "Nothing live" }
)

function Write-Step {
    param([string] $Name)
    Write-Host ""
    Write-Host "=== $Name ==="
}

# Runs a program and returns its lines and exit code. Error output is folded into the lines as text,
# so a program that writes to stderr does not stop the script under Windows PowerShell 5.1.
function Invoke-Native {
    param(
        [string] $File,
        [string[]] $Arguments,
        [switch] $Quiet
    )

    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $lines = New-Object System.Collections.Generic.List[string]
    try {
        & $File @Arguments 2>&1 | ForEach-Object {
            $line = "$_"
            $lines.Add($line)
            if (-not $Quiet) { Write-Host $line }
        }
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    return [pscustomobject]@{ Lines = $lines.ToArray(); Text = ($lines -join "`n"); ExitCode = $code }
}

function Invoke-Dotnet {
    param([string[]] $Arguments, [switch] $Quiet)

    $result = Invoke-Native -File "dotnet" -Arguments $Arguments -Quiet:$Quiet
    if ($result.ExitCode -ne 0) {
        if ($Quiet) { $result.Lines | ForEach-Object { Write-Host $_ } }
        throw "dotnet $($Arguments -join ' ') failed with exit code $($result.ExitCode)."
    }
    return $result
}

function Read-Doc {
    param([string] $RelativePath)

    $path = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) { throw "$RelativePath is missing. The verifier checks it, so it has to exist." }
    return [System.IO.File]::ReadAllText($path)
}

function Assert-That {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

Push-Location $repoRoot
try {
    # ----------------------------------------------------------------------------------------
    Write-Step "Toolchain"
    $sdk = Invoke-Dotnet -Arguments @("--version") -Quiet
    Write-Host "dotnet SDK $($sdk.Text.Trim()) (pinned by global.json)"

    # ----------------------------------------------------------------------------------------
    Write-Step "Restore, locked"
    # --locked-mode fails if any project would resolve a package version its lock file does not name.
    Invoke-Dotnet -Arguments @("restore", "ClaimSeeker.sln", "--locked-mode", "--verbosity", "quiet") | Out-Null

    # ----------------------------------------------------------------------------------------
    Write-Step "Build, warnings as errors"
    $build = Invoke-Dotnet -Arguments @("build", "ClaimSeeker.sln", "-c", $Configuration, "--no-restore", "-warnaserror", "--nologo", "--verbosity", "minimal") -Quiet
    $warningLine = @($build.Lines | Where-Object { $_ -match '^\s*\d+ Warning\(s\)' }) | Select-Object -Last 1
    $errorLine = @($build.Lines | Where-Object { $_ -match '^\s*\d+ Error\(s\)' }) | Select-Object -Last 1
    Assert-That ($null -ne $warningLine -and $warningLine -match '^\s*0 Warning\(s\)') "The build reported warnings. The baseline is 0."
    Assert-That ($null -ne $errorLine -and $errorLine -match '^\s*0 Error\(s\)') "The build reported errors."
    Write-Host "$($warningLine.Trim()), $($errorLine.Trim())"

    # ----------------------------------------------------------------------------------------
    Write-Step "Offline harnesses"
    $measured = [ordered]@{}
    $totalPassed = 0
    $totalFailed = 0
    foreach ($harness in $Harnesses) {
        $project = "tests/$harness/$harness.csproj"
        $run = Invoke-Native -File "dotnet" -Arguments @("run", "--project", $project, "-c", $Configuration, "--no-build") -Quiet
        $summary = @($run.Lines | Where-Object { $_ -match '^=== (\d+) passed, (\d+) failed ===$' }) | Select-Object -Last 1
        if ($null -eq $summary) {
            $run.Lines | ForEach-Object { Write-Host $_ }
            throw "$harness did not print its summary line. It crashed or was cut short (exit code $($run.ExitCode))."
        }

        $null = $summary -match '^=== (\d+) passed, (\d+) failed ===$'
        $passed = [int] $Matches[1]
        $failed = [int] $Matches[2]
        if ($failed -ne 0 -or $run.ExitCode -ne 0) {
            $run.Lines | Where-Object { $_ -match '^\s+FAIL\s' } | ForEach-Object { Write-Host $_ }
            throw "$harness failed: $passed passed, $failed failed (exit code $($run.ExitCode))."
        }

        $measured[$harness] = $passed
        $totalPassed += $passed
        $totalFailed += $failed
        Write-Host ("  {0,-26} {1,4} passed" -f $harness, $passed)
    }

    Write-Host ""
    Write-Host "=== Offline total: $totalPassed passed, $totalFailed failed ==="
    if ($totalPassed -ne $ExpectedOfflineTotal) {
        throw ("The offline total drifted: measured $totalPassed, pinned $ExpectedOfflineTotal. " +
            "A check was added or removed without updating `$ExpectedOfflineTotal in scripts/Verify.ps1 and the harness tables in the docs. " +
            "See 'The doc and verifier drift trap' in CLAUDE.md.")
    }

    # ----------------------------------------------------------------------------------------
    Write-Step "Docs agree with what was measured"
    foreach ($doc in $HarnessTableDocs) {
        $text = Read-Doc $doc
        foreach ($harness in $Harnesses) {
            $row = [regex]::Match($text, '(?m)^\|\s*`?' + [regex]::Escape($harness) + '`?\s*\|\s*([\d,]+)\s*\|')
            Assert-That $row.Success "$doc has no table row for $harness."
            $reported = [int] ($row.Groups[1].Value -replace ',', '')
            Assert-That ($reported -eq $measured[$harness]) "$doc reports $reported checks for $harness; the harness just ran $($measured[$harness])."
        }

        $totalRow = [regex]::Match($text, '(?m)^\|\s*\*\*Total\*\*\s*\|\s*\*\*([\d,]+)\*\*\s*\|')
        Assert-That $totalRow.Success "$doc has no '| **Total** | **N** |' row in its harness table."
        $reportedTotal = [int] ($totalRow.Groups[1].Value -replace ',', '')
        Assert-That ($reportedTotal -eq $totalPassed) "$doc reports a total of $reportedTotal; the harnesses just ran $totalPassed."
        Write-Host "  ${doc}: harness table matches"
    }

    # Any doc that states the total in the standard wording must state the measured one.
    $countDocs = @("README.md", "CLAUDE.md", "AGENTS.md") + @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "docs") -Filter "*.md" -File | ForEach-Object { "docs/$($_.Name)" })
    $statements = 0
    foreach ($doc in $countDocs) {
        $text = Read-Doc $doc
        # \s+ and not a space: a statement that wraps across two lines is still a statement.
        foreach ($match in [regex]::Matches($text, '([\d,]+)\s+offline\s+checks')) {
            $stated = [int] ($match.Groups[1].Value -replace ',', '')
            Assert-That ($stated -eq $totalPassed) "$doc says '$($match.Value -replace '\s+', ' ')'; the harnesses just ran $totalPassed."
            $statements++
        }
    }
    Assert-That ($statements -ge 2) "Expected the README and the project summary to state the total as 'N offline checks'."
    Write-Host "  $statements statements of the total across $($countDocs.Count) docs: all say $totalPassed"

    foreach ($entry in $RequiredPhrases) {
        $text = Read-Doc $entry.File
        Assert-That ($text.Contains($entry.Phrase)) "$($entry.File) no longer says '$($entry.Phrase)'. If the fact changed, change the doc and this list together."
    }
    Write-Host "  $($RequiredPhrases.Count) pinned statements present"

    # ----------------------------------------------------------------------------------------
    Write-Step "No personal data is tracked"
    $git = Get-Command git -ErrorAction SilentlyContinue
    $inRepo = $false
    if ($null -ne $git) {
        $probe = Invoke-Native -File "git" -Arguments @("rev-parse", "--is-inside-work-tree") -Quiet
        $inRepo = ($probe.ExitCode -eq 0)
    }

    if ($inRepo) {
        $tracked = (Invoke-Native -File "git" -Arguments @("ls-files") -Quiet).Lines
        $personal = @($tracked | Where-Object {
            $_ -match '(^|/)household\.json$' -or
            ($_ -match '(^|/)household\.[^/]+\.json$' -and $_ -notmatch '(^|/)household\.example\.json$') -or
            $_ -match '\.db(-wal|-shm)?$' -or
            $_ -match '\.eml$' -or
            $_ -match '^(workspace|\.appdata|secrets)/'
        })
        if ($personal.Count -gt 0) {
            $personal | ForEach-Object { Write-Host "  tracked: $_" }
            throw "Files that hold a person's data are tracked by git. Remove them from the index; .gitignore already names them."
        }
        Write-Host "  $(@($tracked).Count) tracked files, none of them a household file, a database, a letter or a workspace"
    }
    else {
        Write-Host "  skipped: not a git work tree"
    }

    # ----------------------------------------------------------------------------------------
    Write-Step "Engine smoke: the built program, end to end, on recorded answers"
    $workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("claimseeker-verify-" + [guid]::NewGuid().ToString("N"))
    $engine = @("run", "--project", "src/Engine/ClaimSeeker.Engine.csproj", "-c", $Configuration, "--no-build", "--")
    try {
        $init = Invoke-Native -File "dotnet" -Arguments ($engine + @("init", "--workspace", $workspace)) -Quiet
        Assert-That ($init.ExitCode -eq 0 -and $init.Text.Contains("Workspace ready")) "init failed: $($init.Text)"
        Assert-That (Test-Path -LiteralPath (Join-Path $workspace "household.example.json")) "init did not write the example household."

        $import = Invoke-Native -File "dotnet" -Arguments ($engine + @("household", "import", (Join-Path $workspace "household.example.json"), "--workspace", $workspace)) -Quiet
        Assert-That ($import.ExitCode -eq 0 -and $import.Text.Contains("Household stored: 3 people.")) "household import failed: $($import.Text)"

        # A sweep has to be told where to look. With neither --live nor --fixtures it is a usage error.
        $bare = Invoke-Native -File "dotnet" -Arguments ($engine + @("sweep", "--workspace", $workspace)) -Quiet
        Assert-That ($bare.ExitCode -eq 2) "A sweep with no mode should be refused as a usage error; it exited $($bare.ExitCode)."

        $sweep = Invoke-Native -File "dotnet" -Arguments ($engine + @("sweep", "--fixtures", "tests/fixtures/sources", "--workspace", $workspace)) -Quiet
        Assert-That ($sweep.ExitCode -eq 0) "The fixture sweep failed: $($sweep.Text)"
        Assert-That ($sweep.Text.Contains("The network is not used.")) "The fixture sweep did not say it stayed off the network."
        Assert-That ($sweep.Text.Contains('At least $1,889.90 across 10 matches the engine trusts.')) "The fixture sweep's headline changed: $($sweep.Text)"

        $findings = Invoke-Native -File "dotnet" -Arguments ($engine + @("findings", "--workspace", $workspace)) -Quiet
        $first = [regex]::Match($findings.Text, 'f_[0-9a-f]{24}')
        Assert-That ($findings.ExitCode -eq 0 -and $first.Success) "findings listed nothing: $($findings.Text)"

        $draft = Invoke-Native -File "dotnet" -Arguments ($engine + @("draft", $first.Value, "--workspace", $workspace)) -Quiet
        Assert-That ($draft.ExitCode -eq 0) "draft failed: $($draft.Text)"
        $letters = @(Get-ChildItem -LiteralPath (Join-Path $workspace "drafts") -Filter "*.eml" -File)
        Assert-That ($letters.Count -eq 1) "Expected one letter in the drafts folder, found $($letters.Count)."
        $letter = [System.IO.File]::ReadAllText($letters[0].FullName)
        Assert-That ($letter.StartsWith("X-Unsent: 1")) "The letter is not marked unsent."

        $auditRun = Invoke-Native -File "dotnet" -Arguments ($engine + @("audit", "verify", "--workspace", $workspace)) -Quiet
        Assert-That ($auditRun.ExitCode -eq 0 -and $auditRun.Text.Contains("The record is intact")) "audit verify failed: $($auditRun.Text)"

        $doctor = Invoke-Native -File "dotnet" -Arguments ($engine + @("doctor", "--workspace", $workspace)) -Quiet
        Assert-That ($doctor.ExitCode -eq 0) "doctor found a problem: $($doctor.Text)"

        Write-Host "  init, import, sweep on fixtures, findings, draft, audit, doctor: ok"
        Write-Host "  one letter written, marked unsent; nothing was sent and nothing was filed"
    }
    finally {
        if (Test-Path -LiteralPath $workspace) { Remove-Item -LiteralPath $workspace -Recurse -Force }
    }

    # ----------------------------------------------------------------------------------------
    if ($IncludeAudit) {
        Write-Step "Known vulnerabilities (network)"
        $audit = Invoke-Dotnet -Arguments @("list", "ClaimSeeker.sln", "package", "--vulnerable", "--include-transitive") -Quiet
        $clean = @($audit.Lines | Where-Object { $_ -match 'has no vulnerable packages' }).Count
        $vulnerable = @($audit.Lines | Where-Object { $_ -match 'has the following vulnerable packages' })
        if ($vulnerable.Count -gt 0) {
            $audit.Lines | ForEach-Object { Write-Host $_ }
            throw "A package with a known vulnerability is in the dependency graph."
        }
        Assert-That ($clean -gt 0) "The vulnerability check returned nothing to read: $($audit.Text)"
        Write-Host "  $clean projects checked, no known vulnerabilities"
    }

    Write-Host ""
    Write-Host "=== Verify: ok. $totalPassed offline checks passed, 0 failed (pinned: $ExpectedOfflineTotal) ==="
}
finally {
    Pop-Location
}
