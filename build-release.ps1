# Builds a self-contained release zip (no .NET install needed on the target PC) in .\dist.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = ([xml](Get-Content DeepCoolDisplay.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$name = "deepcool-digital-windows-$version-win-x64"
$stage = "dist\$name"

Remove-Item $stage, "dist\$name.zip" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish -c Release -r win-x64 --self-contained true -o "$stage\publish"
if ($LASTEXITCODE) { throw 'dotnet publish failed' }
Remove-Item "$stage\publish\*.pdb" -ErrorAction SilentlyContinue

Copy-Item install.ps1, uninstall.ps1, README.md, LICENSE $stage
Compress-Archive -Path "$stage\*" -DestinationPath "dist\$name.zip"
Remove-Item $stage -Recurse -Force

$zip = Get-Item "dist\$name.zip"
'{0} ({1:N1} MB)' -f $zip.FullName, ($zip.Length / 1MB)
