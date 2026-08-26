[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string]$Version = '1.0.1'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repositoryRoot 'src\BetterCapture.App\BetterCapture.App.csproj'
$publishPath = Join-Path $repositoryRoot 'artifacts\publish\win-x64'
$installerScript = Join-Path $PSScriptRoot 'BetterCapture.iss'
$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
    'C:\Program Files\Inno Setup 7\ISCC.exe',
    'C:\Program Files (x86)\Inno Setup 7\ISCC.exe'
)
$installerCompiler = $compilerCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

if (-not $installerCompiler)
{
    throw 'Inno Setup 7 was not found. Install it with: winget install --id JRSoftware.InnoSetup.7 --exact'
}

if (Test-Path -LiteralPath $publishPath)
{
    $resolvedPublishPath = (Resolve-Path -LiteralPath $publishPath).Path
    $allowedPublishRoot = (Join-Path $repositoryRoot 'artifacts\publish').TrimEnd('\') + '\'
    if (-not $resolvedPublishPath.StartsWith($allowedPublishRoot, [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to clean unexpected publish path: $resolvedPublishPath"
    }

    Remove-Item -LiteralPath $resolvedPublishPath -Recurse -Force
}

dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishReadyToRun=false -o $publishPath --nologo
if ($LASTEXITCODE -ne 0)
{
    throw "BetterCapture Release publish failed with exit code $LASTEXITCODE."
}

& $installerCompiler "/DMyAppVersion=$Version" $installerScript
if ($LASTEXITCODE -ne 0)
{
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installerFileName = "BetterCapture-v$Version-Windows-x64-Setup.exe"
$installerPath = Join-Path $repositoryRoot "artifacts\installer\$installerFileName"
Write-Output "Installer created: $installerPath"
