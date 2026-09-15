$flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Instance
$a = [System.Reflection.Assembly]::LoadFrom('D:\vscode projects\URL_Remover\ILReader\netstandard2.0\ILReader.Core.dll')
$types = $null
try {
    $types = $a.GetTypes()
} catch [System.Reflection.ReflectionTypeLoadException] {
    $ex = $_.ErrorRecord.Exception
    $types = $ex.Types
    Write-Host ("LOADED " + ($types | Measure-Object).Count + " TYPES (some failed to load)")
}
foreach ($t in $types) {
    if (-not $t.IsPublic) { continue }
    Write-Host ("TYPE  " + $t.FullName)
    foreach ($m in $t.GetMethods($flags)) {
        if ($m.DeclaringType -ne $t) { continue }
        try {
            $ps = ($m.GetParameters() | ForEach-Object { $_.ParameterType.Name }) -join ", "
        } catch { $ps = "?" }
        Write-Host ("  M   " + $m.ReturnType.Name + " " + $m.Name + "(" + $ps + ")")
    }
}
