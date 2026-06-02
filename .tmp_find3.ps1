$ErrorActionPreference='Stop'
$paths = @(
  'C:\Users\redoz\.nuget\packages\xunit.v3.common\3.2.2\lib\netstandard2.0\xunit.v3.common.dll',
  'C:\Users\redoz\.nuget\packages\xunit.v3.extensibility.core\3.2.2\lib\netstandard2.0\xunit.v3.core.dll',
  'C:\Users\redoz\.nuget\packages\xunit.v3.runner.common\3.2.2\lib\netstandard2.0\xunit.v3.runner.common.dll'
)
$loaded = foreach($p in $paths){ [System.Reflection.Assembly]::LoadFrom($p) }
foreach($a in $loaded){
  foreach($t in $a.GetExportedTypes()){
    if($t.Name -match 'UniqueIDGenerator|^XunitTest$|XunitTestMethod$|XunitTestClass$|XunitTestCollection$|XunitTestRunnerContext$|XunitTestCaseRunnerContext$|^TestData$|ObsoleteAttribute'){
      Write-Host ("{0}.{1}  [{2}]" -f $t.Namespace, ($t.Name -replace '`.*$',''), $a.GetName().Name)
    }
  }
}
# Check obsolete on Runner.Common.TestPassed
$rc = $loaded | Where-Object { $_.GetName().Name -eq 'xunit.v3.runner.common' }
$tp = $rc.GetType('Xunit.Runner.Common.TestPassed')
if($tp){
  $ob = $tp.GetCustomAttributes($false) | Where-Object { $_.GetType().Name -eq 'ObsoleteAttribute' }
  Write-Host ("Runner.Common.TestPassed obsolete? {0} msg='{1}'" -f ([bool]$ob), ($ob.Message))
  Write-Host ("Runner.Common.TestPassed base: {0}" -f $tp.BaseType.FullName)
}
