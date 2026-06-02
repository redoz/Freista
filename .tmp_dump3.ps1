$ErrorActionPreference = 'Stop'
$paths = @(
  'C:\Users\redoz\.nuget\packages\xunit.v3.common\3.2.2\lib\netstandard2.0\xunit.v3.common.dll',
  'C:\Users\redoz\.nuget\packages\xunit.v3.extensibility.core\3.2.2\lib\netstandard2.0\xunit.v3.core.dll',
  'C:\Users\redoz\.nuget\packages\xunit.v3.runner.common\3.2.2\lib\netstandard2.0\xunit.v3.runner.common.dll'
)
$loaded = foreach($p in $paths){ [System.Reflection.Assembly]::LoadFrom($p) }

function TypeName($t) {
  if ($null -eq $t) { return 'void' }
  if ($t.IsByRef) { return (TypeName $t.GetElementType()) }
  if ($t.IsArray) { return (TypeName $t.GetElementType()) + '[]' }
  if ($t.IsGenericParameter) { return $t.Name }
  $map = @{ 'System.Void'='void';'System.Boolean'='bool';'System.Int32'='int';'System.Int64'='long';'System.String'='string';'System.Object'='object';'System.Decimal'='decimal';'System.Double'='double';'System.Single'='float';'System.Char'='char' }
  $nt = [Nullable]::GetUnderlyingType($t)
  if ($nt) { return (TypeName $nt) + '?' }
  if (-not $t.IsGenericType) {
    if ($map.ContainsKey($t.FullName)) { return $map[$t.FullName] }
    return $t.Name
  }
  $base = $t.Name -replace '`.*$',''
  $args = $t.GetGenericArguments() | ForEach-Object { TypeName $_ }
  return "$base<$($args -join ', ')>"
}
function DumpType($t, $declaredOnly=$true) {
  $kind = if ($t.IsInterface) {'interface'} elseif ($t.IsEnum) {'enum'} elseif ($t.IsValueType) {'struct'} elseif ($t.IsAbstract -and $t.IsSealed) {'static class'} elseif ($t.IsAbstract) {'abstract class'} else {'class'}
  $gen = ''
  if ($t.IsGenericType) { $gen = '<' + (($t.GetGenericArguments() | ForEach-Object { $_.Name }) -join ', ') + '>' }
  Write-Host ""
  Write-Host ("##### {0} {1}{2}   [asm: {3}]   [ns: {4}]" -f $kind, ($t.Name -replace '`.*$',''), $gen, $t.Assembly.GetName().Name, $t.Namespace)
  if ($t.BaseType -and $t.BaseType.FullName -ne 'System.Object' -and -not $t.IsEnum) { Write-Host ("  : base {0}" -f (TypeName $t.BaseType)) }
  $ifaces = $t.GetInterfaces() | ForEach-Object { TypeName $_ }
  if ($ifaces) { Write-Host ("  : implements {0}" -f ($ifaces -join ', ')) }
  if ($t.IsEnum) { foreach ($n in [Enum]::GetNames($t)) { Write-Host ("  {0} = {1}" -f $n, [int][Enum]::Parse($t,$n)) }; return }

  $bf = if($declaredOnly){[System.Reflection.BindingFlags]'Public,Instance,Static,DeclaredOnly'}else{[System.Reflection.BindingFlags]'Public,Instance,Static'}
  foreach ($c in $t.GetConstructors($bf)) {
    $ps = $c.GetParameters() | ForEach-Object {
      $pre = if ($_.ParameterType.IsByRef) { if ($_.IsOut){'out '}else{'ref '} } else {''}
      $def = if ($_.HasDefaultValue) { ' = ' + ($(if($null -eq $_.DefaultValue){'null'}else{$_.DefaultValue})) } else {'' }
      "$pre$(TypeName $_.ParameterType) $($_.Name)$def" }
    Write-Host ("  ctor({0})" -f ($ps -join ', '))
  }
  foreach ($pr in ($t.GetProperties($bf) | Sort-Object Name)) {
    $acc=''
    if ($pr.GetMethod -and $pr.GetMethod.IsPublic) { $acc+='get; ' }
    $sm=$pr.SetMethod
    if ($sm -and $sm.IsPublic) {
      $isInit = $sm.ReturnParameter.GetRequiredCustomModifiers() | Where-Object { $_.Name -eq 'IsExternalInit' }
      if ($isInit){$acc+='init; '}else{$acc+='set; '} }
    $st = if ($pr.GetAccessors($true)[0].IsStatic){'static '}else{''}
    $virt = if ($pr.GetAccessors($true)[0].IsAbstract -and -not $t.IsInterface){'abstract '}elseif($pr.GetAccessors($true)[0].IsVirtual -and -not $pr.GetAccessors($true)[0].IsFinal -and -not $t.IsInterface){'virtual '}else{''}
    Write-Host ("  {0}{1}{2} {3} {{ {4}}}" -f $st,$virt,(TypeName $pr.PropertyType), $pr.Name, $acc)
  }
  foreach ($m in ($t.GetMethods($bf) | Where-Object { -not $_.IsSpecialName } | Sort-Object Name)) {
    $ps = $m.GetParameters() | ForEach-Object {
      $pre = if ($_.ParameterType.IsByRef) { if ($_.IsOut){'out '}else{'ref '} } else {''}
      $def = if ($_.HasDefaultValue) { ' = ' + ($(if($null -eq $_.DefaultValue){'null'}else{$_.DefaultValue})) } else {'' }
      "$pre$(TypeName $_.ParameterType) $($_.Name)$def" }
    $st = if ($m.IsStatic){'static '}else{''}
    $virt = if ($m.IsAbstract -and -not $t.IsInterface){'abstract '}elseif ($m.IsVirtual -and -not $m.IsFinal -and -not $t.IsInterface){'virtual '}else{''}
    $g = if ($m.IsGenericMethod){'<'+(($m.GetGenericArguments()|%{$_.Name}) -join ', ')+'>'}else{''}
    Write-Host ("  {0}{1}{2} {3}{4}({5})" -f $st,$virt,(TypeName $m.ReturnType), $m.Name, $g, ($ps -join ', '))
  }
  foreach ($f in ($t.GetFields($bf) | Where-Object {-not $_.IsSpecialName} | Sort-Object Name)) {
    if ($f.IsLiteral) { Write-Host ("  const {0} {1} = {2}" -f (TypeName $f.FieldType), $f.Name, $f.GetRawConstantValue()) }
    else { $st = if($f.IsStatic){'static '}else{''}; Write-Host ("  {0}{1} {2} (field)" -f $st,(TypeName $f.FieldType), $f.Name) }
  }
}

$args | ForEach-Object {
  $tn = $_
  $declared = $true
  $t = $null
  foreach ($a in $loaded) { $t = $a.GetType($tn); if ($t) { break } }
  if ($t) { DumpType $t $declared } else { Write-Host "NOT FOUND: $tn" }
}
