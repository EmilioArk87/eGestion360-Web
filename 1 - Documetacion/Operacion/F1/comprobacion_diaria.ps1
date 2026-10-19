# Comprobacion diaria de SOLO LECTURA de produccion tras la salida de F1 (eGestion360, eBD_SPD).
# Que hace: (1) sondea el sitio sin sesion, (2) corre el verificador que corresponde a lo aplicado (hoy el de la ventana 3: politica RLS encendida),
# (3) consulta el estado de la base (politica, tamano, eventos, tasas, tenants) y cuenta filas clave POR TENANT fijando el contexto
# de sesion en la misma conexion, y (4) compara con la corrida anterior para avisar si algo baja. No escribe en ninguna tabla.
# La cadena de conexion sale de `dotnet user-secrets` y nunca se imprime. Solo SELECT y sp_set_session_context (contexto de la conexion).
# Salida: resumen en pantalla con OK/ALERTA, archivos en ~/eGestion360-copias-locales/comprobacion-diaria (fuera del repositorio y de OneDrive),
# codigo de salida 0 (todo bien), 1 (hay alertas) o 2 (no pudo correr).
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File .\comprobacion_diaria.ps1 [-Verificador <archivo.sql>]  (sin parametro se elige solo)
param([string]$Verificador = '', [string]$Sitio = 'https://siptecnologia.xyz')
$ErrorActionPreference = 'Stop'
$aqui = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $aqui '..\..\..')
$carpeta = Join-Path $env:USERPROFILE 'eGestion360-copias-locales\comprobacion-diaria'
if ($carpeta -match 'OneDrive') { throw 'La carpeta de resultados no puede estar dentro de OneDrive.' }
if (-not (Test-Path -LiteralPath $carpeta)) { [void](New-Item -ItemType Directory -Path $carpeta) }
$alertas = New-Object System.Collections.Generic.List[string]
$lineas = New-Object System.Collections.Generic.List[string]
function Anotar([string]$estado, [string]$texto) { $l = ('{0,-7} {1}' -f $estado, $texto); $lineas.Add($l); Write-Output $l; if ($estado -eq 'ALERTA') { $alertas.Add($texto) } }

try {
    Anotar 'INFO' ('Comprobacion diaria ' + (Get-Date -Format 'yyyy-MM-dd HH:mm') + ' (hora de esta PC)')

    # ---------- 1) Sitio sin sesion ----------
    $esperado = @(@('/Login', '200'), @('/', '200'), @('/MainMenu', '302'), @('/Admin/Soporte', '302'), @('/Flota/Catalogos/Vehiculos', '302'), @('/app_offline.htm', '404'))
    foreach ($e in $esperado) {
        $r = & curl.exe -s -o NUL -w '%{http_code} %{time_total}' --max-time 30 ($Sitio + $e[0])
        $partes = "$r".Trim().Split(' ')
        if ($partes[0] -eq $e[1]) { Anotar 'OK' ("Sitio {0} -> {1} en {2} s" -f $e[0], $partes[0], $partes[1]) }
        else { Anotar 'ALERTA' ("Sitio {0} dio {1} y se esperaba {2}" -f $e[0], $partes[0], $e[1]) }
        Start-Sleep -Milliseconds 700
    }

    # ---------- 2) Conexion y verificador(es) segun lo que ya este aplicado ----------
    $linea = dotnet user-secrets list --project $repo | Where-Object { $_ -like 'ConnectionStrings:DefaultConnection = *' } | Select-Object -First 1
    if (-not $linea) { throw 'No se encontro la cadena de conexion en user-secrets.' }
    $cn = New-Object System.Data.SqlClient.SqlConnection ($linea.Substring('ConnectionStrings:DefaultConnection = '.Length))
    $cn.Open()
    function Tabla([string]$sql) { $c = $cn.CreateCommand(); $c.CommandText = $sql; $c.CommandTimeout = 60; $t = New-Object System.Data.DataTable; [void](New-Object System.Data.SqlClient.SqlDataAdapter $c).Fill($t); return ,$t }
    function Escalar([string]$sql) { $c = $cn.CreateCommand(); $c.CommandText = $sql; $c.CommandTimeout = 60; return $c.ExecuteScalar() }
    Anotar 'INFO' ('Base: ' + $cn.Database)

    # Cada verificador describe el estado exacto de su etapa: con 038/039/040 aplicados los de la salida de F1 dejan de dar OK por diseno.
    # Sin -Verificador se elige el que corresponde a lo que existe en la base (038 = identidad, 039 = cuentas migradas, 040 = planes).
    $con038 = [int](Escalar "SELECT CASE WHEN OBJECT_ID(N'dbo.accesos_empresa', N'U') IS NOT NULL THEN 1 ELSE 0 END")
    $con039 = if ($con038 -eq 1) { [int](Escalar "SELECT CASE WHEN EXISTS (SELECT 1 FROM saas.cuentas) THEN 1 ELSE 0 END") } else { 0 }
    $con040 = [int](Escalar "SELECT CASE WHEN OBJECT_ID(N'saas.planes', N'U') IS NOT NULL THEN 1 ELSE 0 END")
    if ($Verificador) { $verificadores = @($Verificador) }
    else {
        $verificadores = @()
        if ($con038 -eq 0 -and $con040 -eq 0) { $verificadores += 'verificar_despues_ventana_3.sql' }
        if ($con038 -eq 1 -and $con039 -eq 0) { $verificadores += 'verificar_despues_038.sql' }
        if ($con039 -eq 1) { $verificadores += 'verificar_despues_f15.sql' }
        if ($con040 -eq 1) { $verificadores += 'verificar_despues_f17.sql' }
    }
    $ejec = Join-Path $aqui 'ejecutar_verificacion.ps1'
    foreach ($ver in $verificadores) {
        $salida = & powershell -NoProfile -ExecutionPolicy Bypass -File $ejec -Archivo (Join-Path $aqui $ver) -Destino Produccion 2>&1
        $codigoVer = $LASTEXITCODE
        $resumen = ($salida | Where-Object { $_ -match '^Ejecutor:' } | Select-Object -Last 1)
        if ($codigoVer -eq 0) { Anotar 'OK' ("Verificador {0}: {1}" -f $ver, ($resumen -replace '^Ejecutor:\s*', '')) }
        else {
            Anotar 'ALERTA' ("Verificador {0} con fallas: {1}" -f $ver, ($resumen -replace '^Ejecutor:\s*', ''))
            $salida | Where-Object { $_ -match '^FALLA' -and $_ -notmatch 'RESUMEN' } | Select-Object -First 8 | ForEach-Object { Anotar 'DETALLE' ($_ -replace '\s{2,}', '  ') }
        }
    }

    # ---------- 3) Estado de la base (solo lectura) ----------

    $pol = Tabla "SELECT name, is_enabled FROM sys.security_policies WHERE name = N'pol_tenant'"
    if ($pol.Rows.Count -eq 1 -and $pol.Rows[0].is_enabled) { Anotar 'OK' 'Politica RLS seg.pol_tenant encendida' }
    elseif ($pol.Rows.Count -eq 1) { Anotar 'ALERTA' 'La politica RLS seg.pol_tenant esta APAGADA' }
    else { Anotar 'ALERTA' 'No existe la politica RLS seg.pol_tenant' }

    $mb = Tabla "SELECT SUM(CASE WHEN type = 0 THEN size END) * 8 / 1024 AS datos_mb, SUM(CASE WHEN type = 1 THEN size END) * 8 / 1024 AS log_mb FROM sys.database_files"
    $datosMb = [int]$mb.Rows[0].datos_mb; $logMb = [int]$mb.Rows[0].log_mb
    if ($datosMb -gt 800 -or $logMb -gt 800) { Anotar 'ALERTA' ("Tamano de la base: datos {0} MB y log {1} MB (tope de 1000 MB cada uno)" -f $datosMb, $logMb) }
    else { Anotar 'OK' ("Tamano de la base: datos {0} MB y log {1} MB (tope de 1000 MB cada uno)" -f $datosMb, $logMb) }

    $ev = Tabla "SELECT status, COUNT(*) AS n, MIN(occurred_at) AS mas_antiguo, MAX(intentos) AS max_intentos FROM dbo.domain_events GROUP BY status"
    $pend = $ev | Where-Object { $_.status -ne 'processed' }
    $totalEv = ($ev | Measure-Object n -Sum).Sum
    if (-not $pend) { Anotar 'OK' ("Eventos del outbox: {0} en total, ninguno pendiente ni fallido" -f $totalEv) }
    else { foreach ($p in $pend) { Anotar 'ALERTA' ("Eventos del outbox en estado {0}: {1} (el mas antiguo {2:yyyy-MM-dd HH:mm}, hasta {3} intentos)" -f $p.status, $p.n, $p.mas_antiguo, $p.max_intentos) } }

    $ta = Tabla "SELECT COUNT(*) AS n, MAX(fecha_creacion) AS ultima, DATEDIFF(DAY, MAX(fecha_creacion), SYSDATETIME()) AS dias FROM dbo.tasas_cambio WHERE id_tenant IS NULL"
    $dias = if ($ta.Rows[0].dias -is [DBNull]) { $null } else { [int]$ta.Rows[0].dias }
    if ($null -eq $dias) { Anotar 'ALERTA' 'No hay tasas de cambio globales' }
    elseif ($dias -gt 3) { Anotar 'ALERTA' ("Tasas de cambio globales: {0} filas; la ultima se creo hace {1} dias ({2:yyyy-MM-dd HH:mm}); el job podria estar apagado" -f $ta.Rows[0].n, $dias, $ta.Rows[0].ultima) }
    else { Anotar 'OK' ("Tasas de cambio globales: {0} filas; la ultima se creo hace {1} dias ({2:yyyy-MM-dd HH:mm})" -f $ta.Rows[0].n, $dias, $ta.Rows[0].ultima) }

    $us = Tabla "SELECT COUNT(*) AS total, SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS activos FROM dbo.Users"
    Anotar 'INFO' ("Cuentas en Users: {0} ({1} activas)" -f $us.Rows[0].total, $us.Rows[0].activos)

    # ---------- Conteos por tenant, con el contexto de sesion de cada uno ----------
    $tenants = Tabla "SELECT id_tenant, codigo, estado FROM saas.tenants ORDER BY id_tenant"
    $tablas = @('vehiculos', 'personas', 'cargas_combustible', 'cargos', 'empresa_roles', 'bitacora_cambios')
    $hoy = @{}
    foreach ($t in $tenants.Rows) {
        $c = $cn.CreateCommand()
        $c.CommandText = "EXEC sp_set_session_context @key = N'id_tenant', @value = $([int]$t.id_tenant), @read_only = 0; EXEC sp_set_session_context @key = N'ambito', @value = N'tenant', @read_only = 0;"
        [void]$c.ExecuteNonQuery()
        $fila = [ordered]@{}
        foreach ($tb in $tablas) { $fila[$tb] = [int](Escalar "SELECT COUNT(*) FROM dbo.$tb") }
        $hoy[[string]$t.codigo] = $fila
        Anotar 'INFO' ("Tenant {0} ({1}): {2}" -f $t.codigo, $t.estado, (($fila.GetEnumerator() | ForEach-Object { '{0}={1}' -f $_.Key, $_.Value }) -join ', '))
    }
    $cn.Dispose()

    # ---------- 4) Comparacion con la corrida anterior ----------
    $ultimo = Join-Path $carpeta 'ultimo.json'
    if (Test-Path -LiteralPath $ultimo) {
        $antes = Get-Content -LiteralPath $ultimo -Raw | ConvertFrom-Json
        $bajas = 0
        foreach ($cod in $hoy.Keys) {
            $a = $antes.conteos.$cod
            if ($null -eq $a) { continue }
            foreach ($tb in $hoy[$cod].Keys) {
                $va = $a.$tb
                if ($null -ne $va -and $hoy[$cod][$tb] -lt [int]$va) { $bajas++; Anotar 'ALERTA' ("{0}.{1} bajo de {2} a {3} desde {4}" -f $cod, $tb, $va, $hoy[$cod][$tb], $antes.fecha) }
            }
        }
        if ($bajas -eq 0) { Anotar 'OK' ("Ningun conteo por tenant bajo respecto de la corrida del {0}" -f $antes.fecha) }
    } else { Anotar 'INFO' 'Primera corrida: no hay con que comparar todavia' }
    @{ fecha = (Get-Date -Format 'yyyy-MM-dd HH:mm'); conteos = $hoy } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ultimo -Encoding ASCII

    # ---------- Resumen ----------
    $estadoFinal = if ($alertas.Count -eq 0) { 'TODO BIEN' } else { "$($alertas.Count) ALERTA(S)" }
    Anotar 'RESUMEN' $estadoFinal
    Add-Content -LiteralPath (Join-Path $carpeta 'historial.csv') -Value ('{0},{1},{2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm'), $alertas.Count, $estadoFinal)
    Set-Content -LiteralPath (Join-Path $carpeta ('resultado-{0}.txt' -f (Get-Date -Format 'yyyy-MM-dd'))) -Value $lineas -Encoding ASCII
    if ($alertas.Count -gt 0) { exit 1 } else { exit 0 }
}
catch {
    Write-Output ('ERROR: la comprobacion no pudo correr: ' + $_.Exception.Message)
    exit 2
}
