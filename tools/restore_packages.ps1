# Geri yükleme: MathNet.Numerics ve DocumentFormat.OpenXml paketlerini packages\ klasörüne kopyalar (NuGet önbelleği üzerinden).
$root = Split-Path -Parent $PSScriptRoot
$tmp = Join-Path $env:TEMP 'femrestore'
New-Item -ItemType Directory -Force $tmp | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net472</TargetFramework></PropertyGroup>
<ItemGroup><PackageReference Include="MathNet.Numerics" Version="5.0.0" /><PackageReference Include="DocumentFormat.OpenXml" Version="2.18.0" /></ItemGroup></Project>
'@ | Set-Content (Join-Path $tmp 'r.csproj')
Push-Location $tmp; dotnet restore | Out-Null; Pop-Location
$cache = Join-Path $env:USERPROFILE '.nuget\packages'
Copy-Item (Join-Path $cache 'mathnet.numerics\5.0.0') (Join-Path $root 'packages\MathNet.Numerics.5.0.0') -Recurse -Force
Copy-Item (Join-Path $cache 'documentformat.openxml\2.18.0') (Join-Path $root 'packages\DocumentFormat.OpenXml.2.18.0') -Recurse -Force
Write-Host 'packages hazir'
