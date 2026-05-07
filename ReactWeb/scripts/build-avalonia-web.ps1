$ErrorActionPreference = "Stop"

$reactRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$repoRoot = Resolve-Path (Join-Path $reactRoot "..")
$browserProject = Join-Path $repoRoot "SkalaView.Browser\SkalaView.Browser.csproj"
$browserRoot = Join-Path $repoRoot "SkalaView.Browser"
$target = Join-Path $reactRoot "public\avalonia"

dotnet publish $browserProject -c Release
if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishWwwRoot = Join-Path $browserRoot "bin\Release\net8.0-browser\publish\wwwroot"
$source = $null

if (Test-Path $publishWwwRoot) {
  $source = Resolve-Path $publishWwwRoot
}
else {
  $appBundle = Get-ChildItem $browserRoot -Recurse -Directory -Filter AppBundle |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

  if ($null -ne $appBundle) {
    $source = $appBundle.FullName
  }
}

if ($null -eq $source) {
  throw "Avalonia web output was not found after publish."
}

if (Test-Path $target) {
  Remove-Item $target -Recurse -Force
}

New-Item $target -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $source "*") $target -Recurse -Force

$mainJs = Join-Path $target "main.js"
if (-not (Test-Path $mainJs)) {
  throw "Published Avalonia bundle does not contain main.js."
}

Write-Host "Avalonia web bundle copied to $target"
