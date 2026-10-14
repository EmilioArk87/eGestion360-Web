<#
.SYNOPSIS
  Ejecutor de SOLO LECTURA de los archivos verificar_*.sql de la salida de F1 a produccion.

.DESCRIPTION
  Corre un archivo SQL de comprobaciones contra la base de produccion (Produccion) o contra una base de LocalDB (LocalDb),
  imprime el resultado en texto legible y termina con codigo de salida:
      0 = todas las comprobaciones dieron OK
      1 = hay al menos una FALLA
      2 = el ejecutor no pudo correr (SQL rechazado, sin conexion, error de SQL, sin comprobaciones)

  Garantias de solo lectura:
    * Rechaza cualquier SQL que contenga INSERT, UPDATE, DELETE, MERGE, ALTER, CREATE, DROP, TRUNCATE, EXEC, EXECUTE, GRANT,
      REVOKE o DENY fuera de comentarios, cadenas y nombres entre corchetes (y ademas BACKUP, RESTORE, DBCC, KILL, SHUTDOWN,
      RECONFIGURE, BULK, OPENROWSET, OPENQUERY, OPENDATASOURCE, OPENXML, sp_*, xp_* y SELECT ... INTO una tabla que no sea #temporal).
    * Ejecuta todos los lotes en UNA sola conexion y dentro de una transaccion que SIEMPRE se revierte.
    * No fija SESSION_CONTEXT: la conexion no tiene tenant (es lo que prueba verificar_despues_ventana_3.sql).

  Con -Destino Produccion la cadena de conexion sale de "dotnet user-secrets list" (ConnectionStrings:DefaultConnection): se
  filtra esa unica linea en memoria y NUNCA se imprime ni se imprime el resto de la lista. Solo se muestra el nombre de la base.

.PARAMETER Archivo
  Archivo .sql a ejecutar. Puede ser una ruta o solo el nombre si esta en la misma carpeta que este script.

.PARAMETER Destino
  Produccion (cadena de user-secrets del proyecto) o LocalDb (servidor (localdb)\MSSQLLocalDB con autenticacion de Windows).

.PARAMETER Base
  Nombre de la base de LocalDB. Obligatorio con -Destino LocalDb y no se admite con Produccion.

.EXAMPLE
  .\ejecutar_verificacion.ps1 -Archivo verificar_antes_ventana_1.sql -Destino Produccion

.EXAMPLE
  .\ejecutar_verificacion.ps1 -Archivo verificar_despues_ventana_2.sql -Destino LocalDb -Base egestion_ensayo_f1
#>
param(
    [Parameter(Mandatory = $true)][string]$Archivo,
    [Parameter(Mandatory = $true)][string]$Destino,
    [string]$Base
)

$ErrorActionPreference = 'Stop'

function Salir-Con-Error([string]$mensaje) {
    Write-Host ''
    Write-Host ("ERROR: " + $mensaje) -ForegroundColor Red
    exit 2
}

if (@('Produccion', 'LocalDb') -notcontains $Destino) { Salir-Con-Error "-Destino debe ser Produccion o LocalDb (se recibio: $Destino)." }

# ---------------------------------------------------------------------------------------------------------------------
# 1. Archivo SQL y comprobacion de que solo lee
# ---------------------------------------------------------------------------------------------------------------------
$ruta = $Archivo
if (-not (Test-Path -LiteralPath $ruta)) { $ruta = Join-Path $PSScriptRoot $Archivo }
if (-not (Test-Path -LiteralPath $ruta)) { Salir-Con-Error "No se encontro el archivo SQL: $Archivo" }
$ruta = (Resolve-Path -LiteralPath $ruta).Path
$sql = [IO.File]::ReadAllText($ruta)

# Quita comentarios (-- y /* */), el contenido de las cadenas '...' y los nombres entre [corchetes] o "comillas".
function Limpiar-Sql([string]$texto) {
    $sb = New-Object System.Text.StringBuilder
    $i = 0
    $n = $texto.Length
    while ($i -lt $n) {
        $c = $texto[$i]
        if ($c -eq '-' -and ($i + 1) -lt $n -and $texto[$i + 1] -eq '-') {
            while ($i -lt $n -and $texto[$i] -ne "`n") { $i++ }
            continue
        }
        if ($c -eq '/' -and ($i + 1) -lt $n -and $texto[$i + 1] -eq '*') {
            $nivel = 1
            $i += 2
            while ($i -lt $n -and $nivel -gt 0) {
                if ($texto[$i] -eq '/' -and ($i + 1) -lt $n -and $texto[$i + 1] -eq '*') { $nivel++; $i += 2 }
                elseif ($texto[$i] -eq '*' -and ($i + 1) -lt $n -and $texto[$i + 1] -eq '/') { $nivel--; $i += 2 }
                else { $i++ }
            }
            [void]$sb.Append(' ')
            continue
        }
        if ($c -eq "'") {
            $i++
            while ($i -lt $n) {
                if ($texto[$i] -eq "'") {
                    if (($i + 1) -lt $n -and $texto[$i + 1] -eq "'") { $i += 2; continue }
                    $i++
                    break
                }
                $i++
            }
            [void]$sb.Append(" '' ")
            continue
        }
        if ($c -eq '[') {
            $i++
            while ($i -lt $n -and $texto[$i] -ne ']') { $i++ }
            $i++
            [void]$sb.Append(' [] ')
            continue
        }
        if ($c -eq '"') {
            $i++
            while ($i -lt $n -and $texto[$i] -ne '"') { $i++ }
            $i++
            [void]$sb.Append(' "" ')
            continue
        }
        [void]$sb.Append($c)
        $i++
    }
    return $sb.ToString()
}

$limpio = Limpiar-Sql $sql
$prohibidas = '\b(INSERT|UPDATE|DELETE|MERGE|ALTER|CREATE|DROP|TRUNCATE|EXEC|EXECUTE|GRANT|REVOKE|DENY|BACKUP|RESTORE|DBCC|KILL|SHUTDOWN|RECONFIGURE|BULK|OPENROWSET|OPENQUERY|OPENDATASOURCE|OPENXML|WRITETEXT|UPDATETEXT)\b|\b(sp|xp)_\w*|\bINTO\s+(?!#)'
$hallazgo = [regex]::Match($limpio, $prohibidas, 'IgnoreCase')
if ($hallazgo.Success) {
    $desde = [Math]::Max(0, $hallazgo.Index - 30)
    $largo = [Math]::Min($limpio.Length - $desde, 80)
    $contexto = ($limpio.Substring($desde, $largo) -replace '\s+', ' ').Trim()
    Salir-Con-Error ("El SQL contiene '" + $hallazgo.Value.Trim() + "', que no se admite en una verificacion de solo lectura (cerca de: ..." + $contexto + "...). No se ejecuto nada.")
}

# Lotes separados por GO (los archivos de F1 tienen uno solo)
$lotes = @([regex]::Split($sql, '(?im)^\s*GO\s*$') | Where-Object { (Limpiar-Sql $_).Trim() })
if ($lotes.Count -eq 0) { Salir-Con-Error 'El archivo SQL no tiene ninguna sentencia.' }

# ---------------------------------------------------------------------------------------------------------------------
# 2. Cadena de conexion (jamas se imprime)
# ---------------------------------------------------------------------------------------------------------------------
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if ($Destino -eq 'LocalDb') {
    if (-not $Base) { Salir-Con-Error 'Con -Destino LocalDb hay que indicar -Base <nombre de la base>.' }
    $cadena = "Server=(localdb)\MSSQLLocalDB;Database=$Base;Integrated Security=true;TrustServerCertificate=true"
}
else {
    if ($Base) { Salir-Con-Error '-Base solo se usa con -Destino LocalDb. Con Produccion la base sale de user-secrets.' }
    if (-not (Test-Path -LiteralPath (Join-Path $repo 'eGestion360Web.csproj'))) { Salir-Con-Error 'No se encontro eGestion360Web.csproj subiendo desde esta carpeta.' }
    $lineas = @(& dotnet user-secrets list --project $repo)
    $linea = $lineas | Where-Object { $_ -like 'ConnectionStrings:DefaultConnection = *' } | Select-Object -First 1
    $lineas = $null
    if (-not $linea) { Salir-Con-Error 'No se encontro ConnectionStrings:DefaultConnection en user-secrets.' }
    $cadena = $linea.Substring('ConnectionStrings:DefaultConnection = '.Length)
    $linea = $null
}

try {
    $constructor = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($cadena)
    $constructor['Application Name'] = 'eGestion360 verificacion F1 (solo lectura)'
    $constructor['Connect Timeout'] = 30
    $nombreBase = [string]$constructor['Initial Catalog']
    $cadena = $constructor.ConnectionString
    $constructor = $null
}
catch {
    Salir-Con-Error 'La cadena de conexion no es valida (no se muestra por seguridad).'
}

# ---------------------------------------------------------------------------------------------------------------------
# 3. Ejecucion: una conexion, una transaccion que siempre se revierte
# ---------------------------------------------------------------------------------------------------------------------
Write-Host ''
Write-Host ('Archivo : ' + (Split-Path $ruta -Leaf))
if ($Destino -eq 'Produccion') {
    Write-Host ('Destino : PRODUCCION, base ' + $nombreBase + ' (solo lectura; la transaccion siempre se revierte)') -ForegroundColor Yellow
}
else {
    Write-Host ('Destino : LocalDB, base ' + $nombreBase)
}
Write-Host ('Inicio  : ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))

$resultados = New-Object System.Collections.Generic.List[System.Data.DataTable]
$cronometro = [Diagnostics.Stopwatch]::StartNew()
$cn = New-Object System.Data.SqlClient.SqlConnection $cadena
$cadena = $null
try {
    $cn.Open()
    $tx = $cn.BeginTransaction([System.Data.IsolationLevel]::ReadCommitted)
    try {
        $c0 = $cn.CreateCommand()
        $c0.Transaction = $tx
        $c0.CommandText = 'SET LOCK_TIMEOUT 30000;'
        [void]$c0.ExecuteNonQuery()
        foreach ($lote in $lotes) {
            $cmd = $cn.CreateCommand()
            $cmd.Transaction = $tx
            $cmd.CommandText = $lote
            $cmd.CommandTimeout = 120
            $da = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
            $ds = New-Object System.Data.DataSet
            [void]$da.Fill($ds)
            foreach ($t in $ds.Tables) { $resultados.Add($t) }
        }
    }
    finally {
        try { $tx.Rollback() } catch { }
    }
}
catch {
    $conectado = ($cn.State -eq [System.Data.ConnectionState]::Open)
    $detalle = $_.Exception.Message
    $cn.Dispose()
    if (-not $conectado -and $Destino -eq 'Produccion') {
        # Los mensajes de conexion pueden nombrar el servidor o el usuario: en produccion no se muestran.
        Salir-Con-Error 'No se pudo abrir la conexion con la base de produccion (detalle omitido por seguridad). Revise user-secrets y la red.'
    }
    Salir-Con-Error ('Fallo la ejecucion en la base: ' + $detalle)
}
$cn.Dispose()
$cronometro.Stop()

# ---------------------------------------------------------------------------------------------------------------------
# 4. Salida legible
# ---------------------------------------------------------------------------------------------------------------------
function Partir([string]$texto, [int]$ancho) {
    if ($null -eq $texto) { $texto = '' }
    $partes = New-Object System.Collections.Generic.List[string]
    while ($texto.Length -gt $ancho) {
        $corte = $texto.LastIndexOfAny([char[]]@(' ', ',', ';'), $ancho - 1, $ancho)
        if ($corte -lt ($ancho / 2)) { $corte = $ancho - 1 }
        $partes.Add($texto.Substring(0, $corte + 1).TrimEnd())
        $texto = $texto.Substring($corte + 1).TrimStart()
    }
    $partes.Add($texto)
    return , $partes
}

$ok = 0
$falla = 0
$resumen = $null
foreach ($t in $resultados) {
    $esComprobacion = $t.Columns.Contains('comprobacion') -and $t.Columns.Contains('esperado') -and $t.Columns.Contains('obtenido') -and $t.Columns.Contains('estado')
    Write-Host ''
    if (-not $esComprobacion) {
        Write-Host (($t | Format-Table -AutoSize | Out-String -Width 220).TrimEnd())
        continue
    }
    $w1 = 8; $w2 = 8; $w3 = 8
    foreach ($f in $t.Rows) {
        $w1 = [Math]::Max($w1, ([string]$f['comprobacion']).Length)
        $w2 = [Math]::Max($w2, ([string]$f['esperado']).Length)
        $w3 = [Math]::Max($w3, ([string]$f['obtenido']).Length)
    }
    $w1 = [Math]::Min($w1, 74); $w2 = [Math]::Min($w2, 32); $w3 = [Math]::Min($w3, 44)
    $formato = '{0,-6}  {1,-' + $w1 + '}  {2,-' + $w2 + '}  {3,-' + $w3 + '}'
    Write-Host ($formato -f 'ESTADO', 'COMPROBACION', 'ESPERADO', 'OBTENIDO')
    Write-Host (('-' * 6) + '  ' + ('-' * $w1) + '  ' + ('-' * $w2) + '  ' + ('-' * $w3))
    foreach ($f in $t.Rows) {
        $estado = [string]$f['estado']
        $esResumen = ([string]$f['comprobacion']) -eq 'RESUMEN'
        if ($esResumen) { $resumen = $f }
        elseif ($estado -eq 'OK') { $ok++ }
        else { $falla++ }
        $c1 = Partir ([string]$f['comprobacion']) $w1
        $c2 = Partir ([string]$f['esperado']) $w2
        $c3 = Partir ([string]$f['obtenido']) $w3
        $alto = [Math]::Max($c1.Count, [Math]::Max($c2.Count, $c3.Count))
        for ($k = 0; $k -lt $alto; $k++) {
            $e = ''
            if ($k -eq 0) { $e = $estado }
            $a = ''; if ($k -lt $c1.Count) { $a = $c1[$k] }
            $b = ''; if ($k -lt $c2.Count) { $b = $c2[$k] }
            $d = ''; if ($k -lt $c3.Count) { $d = $c3[$k] }
            $texto = ($formato -f $e, $a, $b, $d).TrimEnd()
            if ($estado -ne 'OK') { Write-Host $texto -ForegroundColor Red }
            elseif ($esResumen) { Write-Host $texto -ForegroundColor Green }
            else { Write-Host $texto }
        }
    }
}

Write-Host ''
Write-Host ('Ejecutor: ' + ($ok + $falla) + ' comprobaciones: ' + $ok + ' OK, ' + $falla + ' FALLA (' + [Math]::Round($cronometro.Elapsed.TotalSeconds, 1) + ' s).')

if (($ok + $falla) -eq 0 -or $null -eq $resumen) {
    Salir-Con-Error 'El SQL no devolvio comprobaciones con la fila RESUMEN: no se puede dar el resultado por bueno.'
}
if ($falla -gt 0 -or ([string]$resumen['estado']) -ne 'OK') {
    Write-Host 'RESULTADO: HAY FALLAS. No siga con el paso siguiente hasta revisarlas.' -ForegroundColor Red
    exit 1
}
Write-Host 'RESULTADO: TODO OK.' -ForegroundColor Green
exit 0
