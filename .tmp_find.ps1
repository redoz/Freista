$ErrorActionPreference='Stop'
$paths = @(
  'C:\Users\redoz\.nuget\packages\xunit.v3.common\3.2.2\lib\netstandard2.0\xunit.v3.common.dll',
  'C:\Users\redoz\.nuget\packages\xunit.v3.extensibility.core\3.2.2\lib\netstandard2.0\xunit.v3.core.dll',
  'C:\Users\redoz\.nuget\packages\xunit.v3.runner.common\3.2.2\lib\netstandard2.0\xunit.v3.runner.common.dll'
)
$loaded = foreach($p in $paths){ [System.Reflection.Assembly]::LoadFrom($p) }
$pats = 'TestCaseDiscoverer','IXunitTestCase','SelfExecuting','XunitTestCase$','ISelfExecuting'
foreach($a in $loaded){
  foreach($t in $a.GetExportedTypes()){
    foreach($pat in $pats){
      if($t.Name -match $pat){ Write-Host ("{0}  ::  {1}  [{2}]" -f $t.Namespace, $t.Name, $a.GetName().Name); break }
    }
  }
}
