# Generates the multi-run fixtures.
#
# Hand-authored rather than recorded: producing a genuinely sharded CI run, a scenario that
# flips outcome between runs, and a scenario that did not exist in the earlier run would mean
# orchestrating several real runs across time. These are about MERGE and MATRIX semantics, not
# about Reqnroll behaviour - the envelope shapes are copied from the recorded fixtures.
#
# Run from the repo root:  pwsh tests/fixtures/multirun/generate.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function New-Envelopes {
    param(
        [string]$Uri,            # feature file uri
        [string]$FeatureName,
        [hashtable[]]$Scenarios, # @{ Name; Status; Attempts (optional) }
        [long]$StartSeconds,
        [string]$RunId
    )

    $lines = New-Object System.Collections.Generic.List[string]

    $lines.Add((@{ meta = @{
        protocolVersion = '30.1.0'
        implementation  = @{ name = 'Reqnroll'; version = '3.3.4' }
        runtime         = @{ name = 'dotNet'; version = '10.0.8' }
        os              = @{ name = 'Windows' }
        cpu             = @{ name = 'x64' }
    } } | ConvertTo-Json -Depth 8 -Compress))

    $lines.Add((@{ testRunStarted = @{
        timestamp = @{ seconds = $StartSeconds; nanos = 0 }
        id        = $RunId
    } } | ConvertTo-Json -Depth 8 -Compress))

    # gherkinDocument: one scenario per entry, two steps each.
    $children = @()
    $i = 0
    foreach ($s in $Scenarios) {
        $i++
        $children += @{ scenario = @{
            id       = "sc-$RunId-$i"
            keyword  = 'Scenario'
            name     = $s.Name
            tags     = @()
            steps    = @(
                @{ id = "st-$RunId-$i-1"; keyword = 'Given '; keywordType = 'Context'; text = 'the system is ready' }
                @{ id = "st-$RunId-$i-2"; keyword = 'Then '; keywordType = 'Outcome'; text = "the outcome of '$($s.Name)' is checked" }
            )
        } }
    }

    $lines.Add((@{ gherkinDocument = @{
        uri     = $Uri
        feature = @{
            location = @{ line = 1 }
            language = 'en'
            keyword  = 'Feature'
            name     = $FeatureName
            children = $children
        }
    } } | ConvertTo-Json -Depth 12 -Compress))

    $i = 0
    foreach ($s in $Scenarios) {
        $i++
        $lines.Add((@{ pickle = @{
            id         = "pk-$RunId-$i"
            uri        = $Uri
            name       = $s.Name
            language   = 'en'
            tags       = @()
            astNodeIds = @("sc-$RunId-$i")
            steps      = @(
                @{ id = "ps-$RunId-$i-1"; text = 'the system is ready'; type = 'Context'; astNodeIds = @("st-$RunId-$i-1") }
                @{ id = "ps-$RunId-$i-2"; text = "the outcome of '$($s.Name)' is checked"; type = 'Outcome'; astNodeIds = @("st-$RunId-$i-2") }
            )
        } } | ConvertTo-Json -Depth 10 -Compress))

        $lines.Add((@{ testCase = @{
            id        = "tc-$RunId-$i"
            pickleId  = "pk-$RunId-$i"
            testSteps = @(
                @{ id = "ts-$RunId-$i-1"; pickleStepId = "ps-$RunId-$i-1" }
                @{ id = "ts-$RunId-$i-2"; pickleStepId = "ps-$RunId-$i-2" }
            )
        } } | ConvertTo-Json -Depth 10 -Compress))
    }

    # Execution. `Attempts` lets one scenario fail then pass inside a single run.
    $clock = $StartSeconds + 1
    $i = 0
    foreach ($s in $Scenarios) {
        $i++
        $attempts = if ($s.ContainsKey('Attempts')) { $s.Attempts } else { @($s.Status) }
        $attempt = 0

        foreach ($status in $attempts) {
            $startedId = "tcs-$RunId-$i-$attempt"

            $lines.Add((@{ testCaseStarted = @{
                id         = $startedId
                testCaseId = "tc-$RunId-$i"
                attempt    = $attempt
                timestamp  = @{ seconds = $clock; nanos = 0 }
            } } | ConvertTo-Json -Depth 8 -Compress))

            $lines.Add((@{ testStepFinished = @{
                testCaseStartedId = $startedId
                testStepId        = "ts-$RunId-$i-1"
                testStepResult    = @{ status = 'PASSED'; duration = @{ seconds = 0; nanos = 1000000 } }
                timestamp         = @{ seconds = $clock; nanos = 500000000 }
            } } | ConvertTo-Json -Depth 8 -Compress))

            $result = @{ status = $status; duration = @{ seconds = 0; nanos = 2000000 } }
            if ($status -eq 'FAILED') {
                $result.message   = "'$($s.Name)' did not hold"
                $result.exception = @{
                    type       = 'AssertionException'
                    message    = "'$($s.Name)' did not hold"
                    stackTrace = '   at Checkout.Steps.TheOutcomeIsChecked()'
                }
            }

            $lines.Add((@{ testStepFinished = @{
                testCaseStartedId = $startedId
                testStepId        = "ts-$RunId-$i-2"
                testStepResult    = $result
                timestamp         = @{ seconds = $clock + 1; nanos = 0 }
            } } | ConvertTo-Json -Depth 8 -Compress))

            $lines.Add((@{ testCaseFinished = @{
                testCaseStartedId = $startedId
                willBeRetried     = ($attempt -lt ($attempts.Count - 1))
                timestamp         = @{ seconds = $clock + 2; nanos = 0 }
            } } | ConvertTo-Json -Depth 8 -Compress))

            $clock += 3
            $attempt++
        }
    }

    $lines.Add((@{ testRunFinished = @{
        success   = $false
        timestamp = @{ seconds = $clock; nanos = 0 }
    } } | ConvertTo-Json -Depth 8 -Compress))

    return $lines
}

$uri = 'Features/Checkout.feature'
$name = 'Checkout'

# --- Run A: 2026-07-26, SHARDED across two agents -------------------------------------------
# Two files, one logical run. Merged they hold four scenarios; treated separately they would
# render as two bogus trend points and a half-empty matrix.
New-Item -ItemType Directory -Force (Join-Path $root '2026-07-26') | Out-Null

$shard1 = New-Envelopes -Uri $uri -FeatureName $name -StartSeconds 1785000000 -RunId 'a1' -Scenarios @(
    @{ Name = 'Add to basket';  Status = 'PASSED' }
    @{ Name = 'Apply discount'; Status = 'PASSED' }
)
$shard2 = New-Envelopes -Uri $uri -FeatureName $name -StartSeconds 1785000030 -RunId 'a2' -Scenarios @(
    @{ Name = 'Checkout'; Status = 'FAILED' }
    @{ Name = 'Refund';   Status = 'PASSED' }
)

Set-Content (Join-Path $root '2026-07-26/shard-1.ndjson') ($shard1 -join "`n") -NoNewline
Set-Content (Join-Path $root '2026-07-26/shard-2.ndjson') ($shard2 -join "`n") -NoNewline

# --- Run B: 2026-07-27, single file ----------------------------------------------------------
# 'Apply discount' flips PASSED -> FAILED and 'Checkout' flips FAILED -> PASSED (both flaky
# across runs). 'Gift cards' is new, so it did NOT RUN in run A. 'Refund' fails then passes
# within this run, which is flaky-within-run.
New-Item -ItemType Directory -Force (Join-Path $root '2026-07-27') | Out-Null

$runB = New-Envelopes -Uri $uri -FeatureName $name -StartSeconds 1785086400 -RunId 'b1' -Scenarios @(
    @{ Name = 'Add to basket';  Status = 'PASSED' }
    @{ Name = 'Apply discount'; Status = 'FAILED' }
    @{ Name = 'Checkout';       Status = 'PASSED' }
    @{ Name = 'Refund';         Status = 'PASSED'; Attempts = @('FAILED', 'PASSED') }
    @{ Name = 'Gift cards';     Status = 'PASSED' }
)

Set-Content (Join-Path $root '2026-07-27/run.ndjson') ($runB -join "`n") -NoNewline

Write-Output "multirun fixtures written to $root"
