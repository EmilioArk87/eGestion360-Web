using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Contabilidad;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Contabilidad
{
    /// <summary>
    /// Núcleo contable (script 010): la base, y no solo el código, impide mezclar datos de dos empresas. Cada relación
    /// interna del módulo es una clave foránea compuesta (id, id_empresa). Las pruebas activan las claves foráneas de
    /// SQLite (la conexión de <see cref="BaseDeDatosDePrueba"/> se abre antes de llegar a EF, que por eso no las activa).
    /// </summary>
    public class NucleoContableTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();

        public void Dispose() => _bd.Dispose();

        private ApplicationDbContext Abrir()
        {
            var db = _bd.Crear();
            db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
            return db;
        }

        private static readonly DateTime Ahora = new(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc);

        private sealed record Base(int Ejercicio, int Periodo, int Cuenta, int OtraCuenta, int Centro);

        /// <summary>Ejercicio 2026, período 10, dos cuentas de movimiento y un centro de costo de una empresa.</summary>
        private Base Sembrar(int empresa)
        {
            using var db = Abrir();
            var ejercicio = new EjercicioFiscal
            {
                IdEmpresa = empresa, Anio = 2026, FechaInicio = new DateTime(2026, 1, 1), FechaFin = new DateTime(2026, 12, 31),
                CreadoPor = "prueba", FechaCreacion = Ahora
            };
            db.Add(ejercicio);
            db.SaveChanges();

            var periodo = new PeriodoContable
            {
                IdEjercicio = ejercicio.IdEjercicio, IdEmpresa = empresa, Numero = 10,
                FechaInicio = new DateTime(2026, 10, 1), FechaFin = new DateTime(2026, 10, 31), CreadoPor = "prueba", FechaCreacion = Ahora
            };
            var caja = Cuenta(empresa, "1101", "Caja");
            var ventas = Cuenta(empresa, "4101", "Ventas", CuentaNaturaleza.Acreedora, CuentaTipo.Ingreso);
            var centro = new CentroCosto { IdEmpresa = empresa, Codigo = "ADM", Nombre = "Administración", CreadoPor = "prueba", FechaCreacion = Ahora };
            db.AddRange(periodo, caja, ventas, centro);
            db.SaveChanges();

            return new Base(ejercicio.IdEjercicio, periodo.IdPeriodo, caja.IdCuenta, ventas.IdCuenta, centro.IdCentroCosto);
        }

        private static CuentaContable Cuenta(int empresa, string codigo, string nombre,
            string naturaleza = CuentaNaturaleza.Deudora, string tipo = CuentaTipo.Activo) => new()
        {
            IdEmpresa = empresa, Codigo = codigo, Nombre = nombre, Naturaleza = naturaleza, Tipo = tipo,
            CreadoPor = "prueba", FechaCreacion = Ahora
        };

        private static Asiento Asiento(int empresa, int periodo, params AsientoMovimiento[] movimientos)
        {
            var asiento = new Asiento
            {
                IdEmpresa = empresa, IdPeriodo = periodo, Fecha = new DateTime(2026, 10, 6), Concepto = "Venta de contado",
                TotalDebito = movimientos.Sum(m => m.Debito), TotalCredito = movimientos.Sum(m => m.Credito),
                CreadoPor = "prueba", FechaCreacion = Ahora
            };
            foreach (var m in movimientos) asiento.Movimientos.Add(m);
            return asiento;
        }

        private static AsientoMovimiento Linea(int empresa, int linea, int cuenta, decimal debito, decimal credito, int? centro = null) => new()
        {
            IdEmpresa = empresa, NumeroLinea = linea, IdCuenta = cuenta, IdCentroCosto = centro, Debito = debito, Credito = credito
        };

        private void Rechaza(Action<ApplicationDbContext> agregar)
        {
            using var db = Abrir();
            agregar(db);
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }

        // ── Camino válido ───────────────────────────────────────────────────

        [Fact]
        public void Asiento_con_cuentas_periodo_y_centro_de_la_misma_empresa_se_guarda()
        {
            var a = Sembrar(DatosBase.EmpresaA);

            using (var db = Abrir())
            {
                db.Add(Asiento(DatosBase.EmpresaA, a.Periodo,
                    Linea(DatosBase.EmpresaA, 1, a.Cuenta, 115m, 0m, a.Centro),
                    Linea(DatosBase.EmpresaA, 2, a.OtraCuenta, 0m, 115m)));
                db.SaveChanges();
            }

            using var lectura = Abrir();
            var guardado = lectura.Asientos.Include(x => x.Movimientos).Single();
            Assert.Equal(2, guardado.Movimientos.Count);
            Assert.All(guardado.Movimientos, m => Assert.Equal(DatosBase.EmpresaA, m.IdEmpresa));
        }

        // ── Aislamiento entre empresas (obligatorio en el proyecto) ─────────

        [Fact]
        public void Movimiento_con_la_cuenta_de_otra_empresa_es_rechazado()
        {
            var a = Sembrar(DatosBase.EmpresaA);
            var b = Sembrar(DatosBase.EmpresaB);

            Rechaza(db => db.Add(Asiento(DatosBase.EmpresaA, a.Periodo,
                Linea(DatosBase.EmpresaA, 1, b.Cuenta, 50m, 0m),          // cuenta de la empresa B
                Linea(DatosBase.EmpresaA, 2, a.OtraCuenta, 0m, 50m))));
        }

        [Fact]
        public void Movimiento_con_el_centro_de_costo_de_otra_empresa_es_rechazado()
        {
            var a = Sembrar(DatosBase.EmpresaA);
            var b = Sembrar(DatosBase.EmpresaB);

            Rechaza(db => db.Add(Asiento(DatosBase.EmpresaA, a.Periodo,
                Linea(DatosBase.EmpresaA, 1, a.Cuenta, 50m, 0m, b.Centro), // centro de la empresa B
                Linea(DatosBase.EmpresaA, 2, a.OtraCuenta, 0m, 50m))));
        }

        [Fact]
        public void Movimiento_marcado_con_otra_empresa_que_la_de_su_asiento_es_rechazado()
        {
            var a = Sembrar(DatosBase.EmpresaA);
            var b = Sembrar(DatosBase.EmpresaB);

            // La línea dice ser de B y usa cuentas de B, pero cuelga de un asiento de A
            Rechaza(db => db.Add(Asiento(DatosBase.EmpresaA, a.Periodo,
                Linea(DatosBase.EmpresaB, 1, b.Cuenta, 50m, 0m),
                Linea(DatosBase.EmpresaB, 2, b.OtraCuenta, 0m, 50m))));
        }

        [Fact]
        public void Asiento_con_el_periodo_de_otra_empresa_es_rechazado()
        {
            Sembrar(DatosBase.EmpresaA);
            var b = Sembrar(DatosBase.EmpresaB);

            Rechaza(db => db.Add(Asiento(DatosBase.EmpresaA, b.Periodo)));   // período de la empresa B
        }

        [Fact]
        public void Periodo_con_el_ejercicio_de_otra_empresa_es_rechazado()
        {
            Sembrar(DatosBase.EmpresaA);
            var b = Sembrar(DatosBase.EmpresaB);

            Rechaza(db => db.Add(new PeriodoContable
            {
                IdEjercicio = b.Ejercicio, IdEmpresa = DatosBase.EmpresaA, Numero = 11,
                FechaInicio = new DateTime(2026, 11, 1), FechaFin = new DateTime(2026, 11, 30), CreadoPor = "prueba", FechaCreacion = Ahora
            }));
        }

        [Fact]
        public void Cuenta_hija_de_una_cuenta_de_otra_empresa_es_rechazada()
        {
            Sembrar(DatosBase.EmpresaA);
            var b = Sembrar(DatosBase.EmpresaB);

            var hija = Cuenta(DatosBase.EmpresaA, "110101", "Caja chica");
            hija.IdCuentaPadre = b.Cuenta;   // padre de la empresa B
            hija.Nivel = 2;
            Rechaza(db => db.Add(hija));
        }

        [Fact]
        public void Cuenta_hija_de_una_cuenta_de_su_misma_empresa_se_guarda()
        {
            var a = Sembrar(DatosBase.EmpresaA);

            using var db = Abrir();
            var hija = Cuenta(DatosBase.EmpresaA, "110101", "Caja chica");
            hija.IdCuentaPadre = a.Cuenta;
            hija.Nivel = 2;
            db.Add(hija);
            db.SaveChanges();

            Assert.Equal(a.Cuenta, db.CuentasContables.AsNoTracking().Single(c => c.Codigo == "110101").IdCuentaPadre);
        }

        // ── Moneda con FK a monedas (017) ───────────────────────────────────

        [Fact]
        public void Cuenta_con_una_moneda_que_no_existe_es_rechazada()
        {
            var cuenta = Cuenta(DatosBase.EmpresaA, "1102", "Bancos");
            cuenta.Moneda = "ZZZ";
            Rechaza(db => db.Add(cuenta));
        }

        // ── Configuración del modelo ────────────────────────────────────────

        [Fact]
        public void El_detalle_no_se_borra_en_cascada_con_el_asiento()
        {
            using var db = _bd.Crear();
            var fk = db.Model.FindEntityType(typeof(AsientoMovimiento))!.GetForeignKeys()
                .Single(f => f.PrincipalEntityType.ClrType == typeof(Asiento));

            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
            Assert.Equal(new[] { "IdAsiento", "IdEmpresa" }, fk.Properties.Select(p => p.Name));
        }

        [Theory]
        [InlineData(typeof(CuentaContable))]
        [InlineData(typeof(EjercicioFiscal))]
        [InlineData(typeof(PeriodoContable))]
        [InlineData(typeof(Asiento))]
        public void Cuentas_ejercicios_periodos_y_asientos_detectan_ediciones_simultaneas(Type tipo)
        {
            var token = tipo.GetProperty("TokenConcurrencia");
            Assert.NotNull(token);
            Assert.NotNull(token!.GetCustomAttributes(typeof(TimestampAttribute), inherit: false).SingleOrDefault());
        }

        [Fact]
        public void La_moneda_de_la_cuenta_es_char_3()
        {
            using var db = _bd.Crear();
            var moneda = db.Model.FindEntityType(typeof(CuentaContable))!.FindProperty(nameof(CuentaContable.Moneda))!;
            Assert.Equal("char(3)", moneda.GetColumnType());
        }
    }
}
