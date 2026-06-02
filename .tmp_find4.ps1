$ErrorActionPreference='Stop'
$paths = @(
  'C:\Users\redoz\.nuget\packages\xunit.v3.common\3.2.2\lib\netstandard2.0\xunit.v3.common.dll',
  'C:\Users\redoz\.nuget\packages\xunit.v3.extensibility.core\3.2.2\lib\netstandard2.0\xunit.v3.core.dll'
)
$loaded = foreach($p in $paths){ [System.Reflection.Assembly]::LoadFrom($p) }
$a = $loaded[1]
foreach($t in $a.GetExportedTypes()){
  if($t.Name -match 'ExecutionErrorTestCase|Discoverer$|TheoryTestCase$|^XunitTestCaseBase'){
    Write-Host ("{0}.{1}" -f $t.Namespace, ($t.Name -replace '`.*$',''))
  }
}
