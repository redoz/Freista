$ErrorActionPreference = 'Stop'
$paths = @(
  'C:\Users\redoz\.nuget\packages\xunit.v3.common\3.2.2\lib\netstandard2.0\xunit.v3.common.dll',
  'C:\Users\redoz\.nuget\packages\xunit.v3.extensibility.core\3.2.2\lib\netstandard2.0\xunit.v3.core.dll',
  'C:\Users\redoz\.nuget\packages\xunit.v3.runner.common\3.2.2\lib\netstandard2.0\xunit.v3.runner.common.dll'
)
$asms = @{}
foreach ($p in $paths) {
  try {
    $a = [System.Reflection.Assembly]::LoadFrom($p)
    $asms[$a.GetName().Name] = $a
    Write-Host "LOADED: $($a.GetName().Name)  types=$($a.GetExportedTypes().Count)"
  } catch {
    Write-Host "FAILED: $p  -> $($_.Exception.Message)"
  }
}
