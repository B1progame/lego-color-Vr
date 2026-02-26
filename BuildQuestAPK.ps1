param(
    [string]$UnityPath = $env:UNITY_EXE,
    [string]$ProjectPath = (Resolve-Path ".").Path,
    [string]$OutputPath = "$(Resolve-Path ".").Path\\Builds\\QuestColorFinder.apk",
    [string]$LogFile = "$(Resolve-Path ".").Path\\Builds\\UnityBuild.log"
)

if ([string]::IsNullOrWhiteSpace($UnityPath)) {
    throw "UnityPath is required. Pass -UnityPath or set UNITY_EXE."
}

$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

Write-Host "Unity:" $UnityPath
Write-Host "Project:" $ProjectPath
Write-Host "Output:" $OutputPath
Write-Host "Log:" $LogFile

& $UnityPath `
    -batchmode `
    -quit `
    -projectPath $ProjectPath `
    -buildTarget Android `
    -executeMethod QuestLegoColorFinder.Editor.BuildQuestAPK.BuildFromCommandLine `
    -logFile $LogFile `
    --outputPath $OutputPath

if ($LASTEXITCODE -ne 0) {
    throw "Unity build failed with exit code $LASTEXITCODE. See $LogFile"
}

Write-Host "APK build completed:" $OutputPath
