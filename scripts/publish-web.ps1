param(
    [ValidateSet('linux-x64', 'linux-arm64', 'win-x64')]
    [string]$Runtime = 'linux-x64'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $repoRoot "artifacts/web-$Runtime"
dotnet publish (Join-Path $repoRoot 'src/JobHunt.Web/JobHunt.Web.csproj') -c Release -r $Runtime --self-contained true -o $outputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar JobHunt.Web.' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $outputDirectory -Force
if ($Runtime.StartsWith('linux-')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot 'deploy/run-web.sh') -Destination $outputDirectory -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'deploy/JobHunt.Web.service') -Destination $outputDirectory -Force
}
Write-Output "Publicação pronta: $outputDirectory"
