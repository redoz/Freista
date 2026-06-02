$ErrorActionPreference='Stop'
$paths = @(
  'C:\Users\redoz\.nuget\packages\xunit.v3.common\3.2.2\lib\netstandard2.0\xunit.v3.common.dll',
  'C:\Users\redoz\.nuget\packages\xunit.v3.extensibility.core\3.2.2\lib\netstandard2.0\xunit.v3.core.dll',
  'C:\Users\redoz\.nuget\packages\xunit.v3.runner.common\3.2.2\lib\netstandard2.0\xunit.v3.runner.common.dll'
)
$loaded = foreach($p in $paths){ [System.Reflection.Assembly]::LoadFrom($p) }
$ise = $loaded[1].GetType('Xunit.v3.ISelfExecutingXunitTestCase')
Write-Host "Types implementing ISelfExecutingXunitTestCase:"
foreach($a in $loaded){
  foreach($t in $a.GetExportedTypes()){
    if($ise.IsAssignableFrom($t) -and $t -ne $ise){ Write-Host ("  {0}.{1}  [{2}]" -f $t.Namespace,$t.Name,$a.GetName().Name) }
  }
}
