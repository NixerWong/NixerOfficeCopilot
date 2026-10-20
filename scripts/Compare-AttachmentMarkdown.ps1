[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CorpusRoot,
    [string]$ManifestName = "manifest.json",
    [string]$MarkdownAssemblyPath = "",
    [string]$MarkItDownCommand = "markitdown",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$corpusPath = (Resolve-Path -LiteralPath $CorpusRoot).Path
$manifestPath = Join-Path $corpusPath $ManifestName
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Corpus manifest not found: $manifestPath"
}

if ([string]::IsNullOrWhiteSpace($MarkdownAssemblyPath)) {
    $MarkdownAssemblyPath = Join-Path $repositoryRoot "OfficeCopilot.Agent.Tests\bin\Debug\OfficeCopilot.Documents.Markdown.dll"
}
$MarkdownAssemblyPath = (Resolve-Path -LiteralPath $MarkdownAssemblyPath).Path
$markdownAssembly = [Reflection.Assembly]::LoadFrom($MarkdownAssemblyPath)
$serviceType = $markdownAssembly.GetType("OfficeCopilot.Documents.Markdown.DocumentMarkdownService", $true)
$service = [Activator]::CreateInstance($serviceType)
$convertMethod = $serviceType.GetMethod("Convert", [Type[]]@([string]))
if ($null -eq $convertMethod) {
    throw "DocumentMarkdownService.Convert(string) was not found in $MarkdownAssemblyPath"
}

$manifest = [IO.File]::ReadAllText($manifestPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
if ($null -eq $manifest.cases -or @($manifest.cases).Count -eq 0) {
    throw "The manifest must contain a non-empty cases array."
}

function Get-EstimatedTokenCount([string]$Text) {
    $wideCount = 0
    $otherCount = 0
    for ($index = 0; $index -lt $Text.Length; $index++) {
        $character = $Text[$index]
        if ([char]::IsHighSurrogate($character) -and $index + 1 -lt $Text.Length -and [char]::IsLowSurrogate($Text[$index + 1])) {
            $wideCount++
            $index++
            continue
        }

        $code = [int]$character
        if (($code -ge 0x2E80 -and $code -le 0x9FFF) -or ($code -ge 0xF900 -and $code -le 0xFAFF) -or ($code -ge 0xFF01 -and $code -le 0xFF60)) {
            $wideCount++
        }
        else {
            $otherCount++
        }
    }
    return [int][Math]::Round($wideCount + ($otherCount / 4.0), 0, [MidpointRounding]::AwayFromZero)
}

function Get-QuestionScore([string]$Markdown, $Questions) {
    $total = 0
    $matched = 0
    foreach ($question in @($Questions)) {
        $terms = @($question.requiredTerms)
        if ($terms.Count -eq 0) {
            continue
        }
        $total++
        $allTermsFound = $true
        foreach ($term in $terms) {
            if ([string]::IsNullOrWhiteSpace([string]$term) -or $Markdown.IndexOf([string]$term, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
                $allTermsFound = $false
                break
            }
        }
        if ($allTermsFound) {
            $matched++
        }
    }
    return [pscustomobject]@{ Total = $total; Matched = $matched }
}

function Invoke-MarkItDown([string]$FilePath) {
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $MarkItDownCommand
    $startInfo.Arguments = '"' + $FilePath + '"'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    if (-not $process.Start()) {
        throw "Could not start MarkItDown command '$MarkItDownCommand'."
    }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stopwatch.Stop()
    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result
    $exitCode = $process.ExitCode
    $process.Dispose()
    if ($exitCode -ne 0) {
        throw "MarkItDown exited with code $exitCode. $($stderr.Trim())"
    }
    return [pscustomobject]@{ Markdown = $stdout; ElapsedMilliseconds = $stopwatch.ElapsedMilliseconds }
}

$rows = New-Object System.Collections.Generic.List[object]
$rootPrefix = $corpusPath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($case in $manifest.cases) {
    $filePath = [IO.Path]::GetFullPath((Join-Path $corpusPath ([string]$case.file)))
    if (-not $filePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "Case file must exist under the corpus root: $($case.file)"
    }
    $questions = @($case.questions)
    if ($questions.Count -eq 0) {
        throw "Case '$($case.id)' has no questions."
    }

    foreach ($engineName in @("Nixer", "MarkItDown")) {
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        $markdown = ""
        $failure = ""
        try {
            if ($engineName -eq "Nixer") {
                $document = $convertMethod.Invoke($service, [object[]]@($filePath))
                $markdown = [string]$document.ToMarkdown()
            }
            else {
                $markItDownResult = Invoke-MarkItDown $filePath
                $markdown = $markItDownResult.Markdown
                $stopwatch.Stop()
                $elapsedMilliseconds = $markItDownResult.ElapsedMilliseconds
            }
        }
        catch {
            $failure = $_.Exception.GetBaseException().Message
        }
        if ($engineName -eq "Nixer" -or $stopwatch.IsRunning) {
            $stopwatch.Stop()
            $elapsedMilliseconds = $stopwatch.ElapsedMilliseconds
        }

        $score = Get-QuestionScore $markdown $questions
        $accuracy = if ($score.Total -eq 0) { 0.0 } else { [Math]::Round(100.0 * $score.Matched / $score.Total, 2) }
        $rows.Add([pscustomobject]@{
            Case = [string]$case.id
            Format = [IO.Path]::GetExtension($filePath).TrimStart('.').ToLowerInvariant()
            Engine = $engineName
            QuestionsMatched = $score.Matched
            QuestionsTotal = $score.Total
            RequiredTermCoveragePercent = $accuracy
            EstimatedTokens = Get-EstimatedTokenCount $markdown
            ElapsedMilliseconds = $elapsedMilliseconds
            Error = $failure
        })
    }
}

$summary = @($rows | Group-Object Engine | ForEach-Object {
    $engineRows = @($_.Group)
    $successfulRows = @($engineRows | Where-Object { [string]::IsNullOrWhiteSpace($_.Error) })
    $questionTotal = ($successfulRows | Measure-Object QuestionsTotal -Sum).Sum
    $questionMatched = ($successfulRows | Measure-Object QuestionsMatched -Sum).Sum
    [pscustomobject]@{
        Engine = $_.Name
        Cases = $engineRows.Count
        FailedCases = @($engineRows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Error) }).Count
        RequiredTermCoveragePercent = if (-not $questionTotal) { 0.0 } else { [Math]::Round(100.0 * $questionMatched / $questionTotal, 2) }
        AverageEstimatedTokens = if ($successfulRows.Count -eq 0) { 0 } else { [Math]::Round(($successfulRows | Measure-Object EstimatedTokens -Average).Average, 2) }
        AverageMilliseconds = if ($successfulRows.Count -eq 0) { 0 } else { [Math]::Round(($successfulRows | Measure-Object ElapsedMilliseconds -Average).Average, 2) }
    }
})

$summary | Format-Table -AutoSize
$rows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Error) } | Format-Table Case, Engine, Error -Wrap
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputFile = [IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = Split-Path -Parent $outputFile
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
        [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    }
    [IO.File]::WriteAllText($outputFile, (ConvertTo-Json -InputObject @($rows) -Depth 6), [Text.UTF8Encoding]::new($false))
}