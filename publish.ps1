# Gera o executável do Macro Midas em .\publish
#   .\publish.ps1                 -> um único MacroMidas.exe (precisa do .NET 9 Desktop Runtime instalado)
#   .\publish.ps1 -SelfContained  -> exe maior, roda em qualquer Windows 10/11 x64 sem instalar nada
param([switch]$SelfContained)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'publish'
$sc = if ($SelfContained) { 'true' } else { 'false' }

dotnet publish (Join-Path $root 'src\MacroMidas\MacroMidas.csproj') `
    -c Release -r win-x64 --self-contained $sc `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
    -o $out

if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou ($LASTEXITCODE)" }
Write-Host ""
Write-Host "Pronto: $out\MacroMidas.exe" -ForegroundColor Yellow
