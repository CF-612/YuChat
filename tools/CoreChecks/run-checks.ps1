param([string]$DotnetRoot = 'C:\Program Files\dotnet')
$ErrorActionPreference = 'Stop'
$taskBuild = Join-Path $PSScriptRoot '.build'
New-Item -ItemType Directory -Force -Path $taskBuild | Out-Null
$dotnet = Join-Path $DotnetRoot 'dotnet.exe'
$sdk = Get-ChildItem -LiteralPath (Join-Path $DotnetRoot 'sdk') -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if ($null -eq $sdk) { throw 'An installed .NET SDK is required.' }
$compiler = Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll'
$net8 = Get-ChildItem -LiteralPath (Join-Path $DotnetRoot 'packs\Microsoft.NETCore.App.Ref') -Directory |
    Where-Object { $_.Name -match '^8\.0\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if ($null -eq $net8) { throw 'The installed .NET 8 reference pack is required.' }
$standardRefs = Join-Path $DotnetRoot 'packs\NETStandard.Library.Ref\2.1.0\ref\netstandard2.1'
if (!(Test-Path -LiteralPath $standardRefs)) { throw 'The installed .NET Standard 2.1 reference pack is required.' }
$core = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\LingChat-XR\Assets\LingChatXR\Core'))
$sources = @(Get-ChildItem -LiteralPath $core -Filter '*.cs' | ForEach-Object { '"' + $_.FullName + '"' })

# Use the installed compiler/reference packs directly: no package sources, NuGet config,
# restore, network, user PATH writes, or certificate setup are needed.
$compatDll = Join-Path $taskBuild 'LingChatXR.Core.dll'
$compatArgs = @('-nologo', '-target:library', '-langversion:9.0', '-nostdlib+', '-warnaserror+', ('-out:"' + $compatDll + '"'))
$compatArgs += @(Get-ChildItem -LiteralPath $standardRefs -Filter '*.dll' | ForEach-Object { '-reference:"' + $_.FullName + '"' })
$compatArgs += $sources
$compatResponse = Join-Path $taskBuild 'compatibility.rsp'
Set-Content -LiteralPath $compatResponse -Value $compatArgs -Encoding utf8
& $dotnet $compiler ('@' + $compatResponse)
if ($LASTEXITCODE -ne 0) { throw 'C# 9 / .NET Standard 2.1 compatibility compile failed.' }
Write-Output 'PASS: C# 9 / .NET Standard 2.1 core compilation (warnings treated as errors)'

$testDll = Join-Path $taskBuild 'CoreChecks.dll'
$testArgs = @('-nologo', '-target:exe', '-langversion:9.0', '-nostdlib+', '-warnaserror+', ('-out:"' + $testDll + '"'))
$testArgs += @(Get-ChildItem -LiteralPath (Join-Path $net8.FullName 'ref\net8.0') -Filter '*.dll' | ForEach-Object { '-reference:"' + $_.FullName + '"' })
$testArgs += '-reference:"' + $compatDll + '"'
$testArgs += '"' + (Join-Path $PSScriptRoot 'Program.cs') + '"'
$testResponse = Join-Path $taskBuild 'checks.rsp'
Set-Content -LiteralPath $testResponse -Value $testArgs -Encoding utf8
& $dotnet $compiler ('@' + $testResponse)
if ($LASTEXITCODE -ne 0) { throw 'Behavior-check compilation failed.' }
Set-Content -LiteralPath (Join-Path $taskBuild 'CoreChecks.runtimeconfig.json') -Encoding utf8 -Value '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}'
& $dotnet $testDll
if ($LASTEXITCODE -ne 0) { throw 'Behavior checks failed.' }
