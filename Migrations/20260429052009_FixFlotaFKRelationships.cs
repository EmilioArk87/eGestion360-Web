using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eGestion360Web.Migrations
{
    /// <inheritdoc />
    public partial class FixFlotaFKRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Las shadow columns (TipoVehiculoIdTipoVehiculo, RutaIdRuta, etc.) nunca
            // existieron en la BD porque las tablas se crearon con columnas explícitas.
            // Solo necesitamos crear los índices y FKs correctos si no existen aún.

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_vehiculos_id_ruta' AND object_id=OBJECT_ID('vehiculos'))
                    CREATE INDEX [IX_vehiculos_id_ruta] ON [vehiculos]([id_ruta]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_vehiculos_id_tipo_vehiculo' AND object_id=OBJECT_ID('vehiculos'))
                    CREATE INDEX [IX_vehiculos_id_tipo_vehiculo] ON [vehiculos]([id_tipo_vehiculo]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_salarios_diarios_id_persona' AND object_id=OBJECT_ID('salarios_diarios'))
                    CREATE INDEX [IX_salarios_diarios_id_persona] ON [salarios_diarios]([id_persona]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_salarios_diarios_id_vehiculo' AND object_id=OBJECT_ID('salarios_diarios'))
                    CREATE INDEX [IX_salarios_diarios_id_vehiculo] ON [salarios_diarios]([id_vehiculo]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_polizas_seguros_id_vehiculo' AND object_id=OBJECT_ID('polizas_seguros'))
                    CREATE INDEX [IX_polizas_seguros_id_vehiculo] ON [polizas_seguros]([id_vehiculo]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ordenes_mantenimiento_id_taller' AND object_id=OBJECT_ID('ordenes_mantenimiento'))
                    CREATE INDEX [IX_ordenes_mantenimiento_id_taller] ON [ordenes_mantenimiento]([id_taller]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ordenes_mantenimiento_id_vehiculo' AND object_id=OBJECT_ID('ordenes_mantenimiento'))
                    CREATE INDEX [IX_ordenes_mantenimiento_id_vehiculo] ON [ordenes_mantenimiento]([id_vehiculo]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_odometro_diario_id_conductor' AND object_id=OBJECT_ID('odometro_diario'))
                    CREATE INDEX [IX_odometro_diario_id_conductor] ON [odometro_diario]([id_conductor]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_odometro_diario_id_ruta' AND object_id=OBJECT_ID('odometro_diario'))
                    CREATE INDEX [IX_odometro_diario_id_ruta] ON [odometro_diario]([id_ruta]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_odometro_diario_id_vehiculo' AND object_id=OBJECT_ID('odometro_diario'))
                    CREATE INDEX [IX_odometro_diario_id_vehiculo] ON [odometro_diario]([id_vehiculo]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_gastos_repuestos_id_categoria_repuesto' AND object_id=OBJECT_ID('gastos_repuestos'))
                    CREATE INDEX [IX_gastos_repuestos_id_categoria_repuesto] ON [gastos_repuestos]([id_categoria_repuesto]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_gastos_repuestos_id_vehiculo' AND object_id=OBJECT_ID('gastos_repuestos'))
                    CREATE INDEX [IX_gastos_repuestos_id_vehiculo] ON [gastos_repuestos]([id_vehiculo]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_cargas_combustible_id_conductor' AND object_id=OBJECT_ID('cargas_combustible'))
                    CREATE INDEX [IX_cargas_combustible_id_conductor] ON [cargas_combustible]([id_conductor]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_cargas_combustible_id_vehiculo' AND object_id=OBJECT_ID('cargas_combustible'))
                    CREATE INDEX [IX_cargas_combustible_id_vehiculo] ON [cargas_combustible]([id_vehiculo]);
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_cargas_combustible_personas_id_conductor')
                    ALTER TABLE [cargas_combustible] ADD CONSTRAINT [FK_cargas_combustible_personas_id_conductor]
                        FOREIGN KEY ([id_conductor]) REFERENCES [personas]([id_persona]) ON DELETE SET NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_cargas_combustible_vehiculos_id_vehiculo')
                    ALTER TABLE [cargas_combustible] ADD CONSTRAINT [FK_cargas_combustible_vehiculos_id_vehiculo]
                        FOREIGN KEY ([id_vehiculo]) REFERENCES [vehiculos]([id_vehiculo]) ON DELETE NO ACTION;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_gastos_repuestos_categorias_repuesto_id_categoria_repuesto')
                    ALTER TABLE [gastos_repuestos] ADD CONSTRAINT [FK_gastos_repuestos_categorias_repuesto_id_categoria_repuesto]
                        FOREIGN KEY ([id_categoria_repuesto]) REFERENCES [categorias_repuesto]([id_categoria_repuesto]) ON DELETE NO ACTION;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_gastos_repuestos_vehiculos_id_vehiculo')
                    ALTER TABLE [gastos_repuestos] ADD CONSTRAINT [FK_gastos_repuestos_vehiculos_id_vehiculo]
                        FOREIGN KEY ([id_vehiculo]) REFERENCES [vehiculos]([id_vehiculo]) ON DELETE NO ACTION;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_odometro_diario_personas_id_conductor')
                    ALTER TABLE [odometro_diario] ADD CONSTRAINT [FK_odometro_diario_personas_id_conductor]
                        FOREIGN KEY ([id_conductor]) REFERENCES [personas]([id_persona]) ON DELETE SET NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_odometro_diario_rutas_id_ruta')
                    ALTER TABLE [odometro_diario] ADD CONSTRAINT [FK_odometro_diario_rutas_id_ruta]
                        FOREIGN KEY ([id_ruta]) REFERENCES [rutas]([id_ruta]) ON DELETE SET NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_odometro_diario_vehiculos_id_vehiculo')
                    ALTER TABLE [odometro_diario] ADD CONSTRAINT [FK_odometro_diario_vehiculos_id_vehiculo]
                        FOREIGN KEY ([id_vehiculo]) REFERENCES [vehiculos]([id_vehiculo]) ON DELETE NO ACTION;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_ordenes_mantenimiento_talleres_id_taller')
                    ALTER TABLE [ordenes_mantenimiento] ADD CONSTRAINT [FK_ordenes_mantenimiento_talleres_id_taller]
                        FOREIGN KEY ([id_taller]) REFERENCES [talleres]([id_taller]) ON DELETE NO ACTION;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_ordenes_mantenimiento_vehiculos_id_vehiculo')
                    ALTER TABLE [ordenes_mantenimiento] ADD CONSTRAINT [FK_ordenes_mantenimiento_vehiculos_id_vehiculo]
                        FOREIGN KEY ([id_vehiculo]) REFERENCES [vehiculos]([id_vehiculo]) ON DELETE NO ACTION;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_polizas_seguros_vehiculos_id_vehiculo')
                    ALTER TABLE [polizas_seguros] ADD CONSTRAINT [FK_polizas_seguros_vehiculos_id_vehiculo]
                        FOREIGN KEY ([id_vehiculo]) REFERENCES [vehiculos]([id_vehiculo]) ON DELETE NO ACTION;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_salarios_diarios_personas_id_persona')
                    ALTER TABLE [salarios_diarios] ADD CONSTRAINT [FK_salarios_diarios_personas_id_persona]
                        FOREIGN KEY ([id_persona]) REFERENCES [personas]([id_persona]) ON DELETE NO ACTION;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_salarios_diarios_vehiculos_id_vehiculo')
                    ALTER TABLE [salarios_diarios] ADD CONSTRAINT [FK_salarios_diarios_vehiculos_id_vehiculo]
                        FOREIGN KEY ([id_vehiculo]) REFERENCES [vehiculos]([id_vehiculo]) ON DELETE NO ACTION;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_vehiculos_rutas_id_ruta')
                    ALTER TABLE [vehiculos] ADD CONSTRAINT [FK_vehiculos_rutas_id_ruta]
                        FOREIGN KEY ([id_ruta]) REFERENCES [rutas]([id_ruta]) ON DELETE SET NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_vehiculos_tipos_vehiculo_id_tipo_vehiculo')
                    ALTER TABLE [vehiculos] ADD CONSTRAINT [FK_vehiculos_tipos_vehiculo_id_tipo_vehiculo]
                        FOREIGN KEY ([id_tipo_vehiculo]) REFERENCES [tipos_vehiculo]([id_tipo_vehiculo]) ON DELETE NO ACTION;
            ");

            // Resto del Up() original eliminado — los DropForeignKey/DropIndex/DropColumn
            // apuntaban a shadow columns que nunca existieron en esta base de datos.
            return;

            migrationBuilder.DropForeignKey(
                name: "FK_cargas_combustible_personas_ConductorIdPersona",
                table: "cargas_combustible");

            migrationBuilder.DropForeignKey(
                name: "FK_cargas_combustible_vehiculos_VehiculoIdVehiculo",
                table: "cargas_combustible");

            migrationBuilder.DropForeignKey(
                name: "FK_gastos_repuestos_categorias_repuesto_CategoriaRepuestoIdCategoriaRepuesto",
                table: "gastos_repuestos");

            migrationBuilder.DropForeignKey(
                name: "FK_gastos_repuestos_vehiculos_VehiculoIdVehiculo",
                table: "gastos_repuestos");

            migrationBuilder.DropForeignKey(
                name: "FK_odometro_diario_personas_ConductorIdPersona",
                table: "odometro_diario");

            migrationBuilder.DropForeignKey(
                name: "FK_odometro_diario_rutas_RutaIdRuta",
                table: "odometro_diario");

            migrationBuilder.DropForeignKey(
                name: "FK_odometro_diario_vehiculos_VehiculoIdVehiculo",
                table: "odometro_diario");

            migrationBuilder.DropForeignKey(
                name: "FK_ordenes_mantenimiento_talleres_TallerIdTaller",
                table: "ordenes_mantenimiento");

            migrationBuilder.DropForeignKey(
                name: "FK_ordenes_mantenimiento_vehiculos_VehiculoIdVehiculo",
                table: "ordenes_mantenimiento");

            migrationBuilder.DropForeignKey(
                name: "FK_polizas_seguros_vehiculos_VehiculoIdVehiculo",
                table: "polizas_seguros");

            migrationBuilder.DropForeignKey(
                name: "FK_salarios_diarios_personas_PersonaIdPersona",
                table: "salarios_diarios");

            migrationBuilder.DropForeignKey(
                name: "FK_salarios_diarios_vehiculos_VehiculoIdVehiculo",
                table: "salarios_diarios");

            migrationBuilder.DropForeignKey(
                name: "FK_vehiculos_rutas_RutaIdRuta",
                table: "vehiculos");

            migrationBuilder.DropForeignKey(
                name: "FK_vehiculos_tipos_vehiculo_TipoVehiculoIdTipoVehiculo",
                table: "vehiculos");

            migrationBuilder.DropIndex(
                name: "IX_vehiculos_RutaIdRuta",
                table: "vehiculos");

            migrationBuilder.DropIndex(
                name: "IX_vehiculos_TipoVehiculoIdTipoVehiculo",
                table: "vehiculos");

            migrationBuilder.DropIndex(
                name: "IX_salarios_diarios_PersonaIdPersona",
                table: "salarios_diarios");

            migrationBuilder.DropIndex(
                name: "IX_salarios_diarios_VehiculoIdVehiculo",
                table: "salarios_diarios");

            migrationBuilder.DropIndex(
                name: "IX_polizas_seguros_VehiculoIdVehiculo",
                table: "polizas_seguros");

            migrationBuilder.DropIndex(
                name: "IX_ordenes_mantenimiento_TallerIdTaller",
                table: "ordenes_mantenimiento");

            migrationBuilder.DropIndex(
                name: "IX_ordenes_mantenimiento_VehiculoIdVehiculo",
                table: "ordenes_mantenimiento");

            migrationBuilder.DropIndex(
                name: "IX_odometro_diario_ConductorIdPersona",
                table: "odometro_diario");

            migrationBuilder.DropIndex(
                name: "IX_odometro_diario_RutaIdRuta",
                table: "odometro_diario");

            migrationBuilder.DropIndex(
                name: "IX_odometro_diario_VehiculoIdVehiculo",
                table: "odometro_diario");

            migrationBuilder.DropIndex(
                name: "IX_gastos_repuestos_CategoriaRepuestoIdCategoriaRepuesto",
                table: "gastos_repuestos");

            migrationBuilder.DropIndex(
                name: "IX_gastos_repuestos_VehiculoIdVehiculo",
                table: "gastos_repuestos");

            migrationBuilder.DropIndex(
                name: "IX_cargas_combustible_ConductorIdPersona",
                table: "cargas_combustible");

            migrationBuilder.DropIndex(
                name: "IX_cargas_combustible_VehiculoIdVehiculo",
                table: "cargas_combustible");

            migrationBuilder.DropColumn(
                name: "RutaIdRuta",
                table: "vehiculos");

            migrationBuilder.DropColumn(
                name: "TipoVehiculoIdTipoVehiculo",
                table: "vehiculos");

            migrationBuilder.DropColumn(
                name: "PersonaIdPersona",
                table: "salarios_diarios");

            migrationBuilder.DropColumn(
                name: "VehiculoIdVehiculo",
                table: "salarios_diarios");

            migrationBuilder.DropColumn(
                name: "VehiculoIdVehiculo",
                table: "polizas_seguros");

            migrationBuilder.DropColumn(
                name: "TallerIdTaller",
                table: "ordenes_mantenimiento");

            migrationBuilder.DropColumn(
                name: "VehiculoIdVehiculo",
                table: "ordenes_mantenimiento");

            migrationBuilder.DropColumn(
                name: "ConductorIdPersona",
                table: "odometro_diario");

            migrationBuilder.DropColumn(
                name: "RutaIdRuta",
                table: "odometro_diario");

            migrationBuilder.DropColumn(
                name: "VehiculoIdVehiculo",
                table: "odometro_diario");

            migrationBuilder.DropColumn(
                name: "CategoriaRepuestoIdCategoriaRepuesto",
                table: "gastos_repuestos");

            migrationBuilder.DropColumn(
                name: "VehiculoIdVehiculo",
                table: "gastos_repuestos");

            migrationBuilder.DropColumn(
                name: "ConductorIdPersona",
                table: "cargas_combustible");

            migrationBuilder.DropColumn(
                name: "VehiculoIdVehiculo",
                table: "cargas_combustible");

            migrationBuilder.CreateIndex(
                name: "IX_vehiculos_id_ruta",
                table: "vehiculos",
                column: "id_ruta");

            migrationBuilder.CreateIndex(
                name: "IX_vehiculos_id_tipo_vehiculo",
                table: "vehiculos",
                column: "id_tipo_vehiculo");

            migrationBuilder.CreateIndex(
                name: "IX_salarios_diarios_id_persona",
                table: "salarios_diarios",
                column: "id_persona");

            migrationBuilder.CreateIndex(
                name: "IX_salarios_diarios_id_vehiculo",
                table: "salarios_diarios",
                column: "id_vehiculo");

            migrationBuilder.CreateIndex(
                name: "IX_polizas_seguros_id_vehiculo",
                table: "polizas_seguros",
                column: "id_vehiculo");

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_mantenimiento_id_taller",
                table: "ordenes_mantenimiento",
                column: "id_taller");

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_mantenimiento_id_vehiculo",
                table: "ordenes_mantenimiento",
                column: "id_vehiculo");

            migrationBuilder.CreateIndex(
                name: "IX_odometro_diario_id_conductor",
                table: "odometro_diario",
                column: "id_conductor");

            migrationBuilder.CreateIndex(
                name: "IX_odometro_diario_id_ruta",
                table: "odometro_diario",
                column: "id_ruta");

            migrationBuilder.CreateIndex(
                name: "IX_odometro_diario_id_vehiculo",
                table: "odometro_diario",
                column: "id_vehiculo");

            migrationBuilder.CreateIndex(
                name: "IX_gastos_repuestos_id_categoria_repuesto",
                table: "gastos_repuestos",
                column: "id_categoria_repuesto");

            migrationBuilder.CreateIndex(
                name: "IX_gastos_repuestos_id_vehiculo",
                table: "gastos_repuestos",
                column: "id_vehiculo");

            migrationBuilder.CreateIndex(
                name: "IX_cargas_combustible_id_conductor",
                table: "cargas_combustible",
                column: "id_conductor");

            migrationBuilder.CreateIndex(
                name: "IX_cargas_combustible_id_vehiculo",
                table: "cargas_combustible",
                column: "id_vehiculo");

            migrationBuilder.AddForeignKey(
                name: "FK_cargas_combustible_personas_id_conductor",
                table: "cargas_combustible",
                column: "id_conductor",
                principalTable: "personas",
                principalColumn: "id_persona",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_cargas_combustible_vehiculos_id_vehiculo",
                table: "cargas_combustible",
                column: "id_vehiculo",
                principalTable: "vehiculos",
                principalColumn: "id_vehiculo",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_gastos_repuestos_categorias_repuesto_id_categoria_repuesto",
                table: "gastos_repuestos",
                column: "id_categoria_repuesto",
                principalTable: "categorias_repuesto",
                principalColumn: "id_categoria_repuesto",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_gastos_repuestos_vehiculos_id_vehiculo",
                table: "gastos_repuestos",
                column: "id_vehiculo",
                principalTable: "vehiculos",
                principalColumn: "id_vehiculo",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_odometro_diario_personas_id_conductor",
                table: "odometro_diario",
                column: "id_conductor",
                principalTable: "personas",
                principalColumn: "id_persona",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_odometro_diario_rutas_id_ruta",
                table: "odometro_diario",
                column: "id_ruta",
                principalTable: "rutas",
                principalColumn: "id_ruta",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_odometro_diario_vehiculos_id_vehiculo",
                table: "odometro_diario",
                column: "id_vehiculo",
                principalTable: "vehiculos",
                principalColumn: "id_vehiculo",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ordenes_mantenimiento_talleres_id_taller",
                table: "ordenes_mantenimiento",
                column: "id_taller",
                principalTable: "talleres",
                principalColumn: "id_taller",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ordenes_mantenimiento_vehiculos_id_vehiculo",
                table: "ordenes_mantenimiento",
                column: "id_vehiculo",
                principalTable: "vehiculos",
                principalColumn: "id_vehiculo",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_polizas_seguros_vehiculos_id_vehiculo",
                table: "polizas_seguros",
                column: "id_vehiculo",
                principalTable: "vehiculos",
                principalColumn: "id_vehiculo",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_salarios_diarios_personas_id_persona",
                table: "salarios_diarios",
                column: "id_persona",
                principalTable: "personas",
                principalColumn: "id_persona",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_salarios_diarios_vehiculos_id_vehiculo",
                table: "salarios_diarios",
                column: "id_vehiculo",
                principalTable: "vehiculos",
                principalColumn: "id_vehiculo",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vehiculos_rutas_id_ruta",
                table: "vehiculos",
                column: "id_ruta",
                principalTable: "rutas",
                principalColumn: "id_ruta",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_vehiculos_tipos_vehiculo_id_tipo_vehiculo",
                table: "vehiculos",
                column: "id_tipo_vehiculo",
                principalTable: "tipos_vehiculo",
                principalColumn: "id_tipo_vehiculo",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cargas_combustible_personas_id_conductor",
                table: "cargas_combustible");

            migrationBuilder.DropForeignKey(
                name: "FK_cargas_combustible_vehiculos_id_vehiculo",
                table: "cargas_combustible");

            migrationBuilder.DropForeignKey(
                name: "FK_gastos_repuestos_categorias_repuesto_id_categoria_repuesto",
                table: "gastos_repuestos");

            migrationBuilder.DropForeignKey(
                name: "FK_gastos_repuestos_vehiculos_id_vehiculo",
                table: "gastos_repuestos");

            migrationBuilder.DropForeignKey(
                name: "FK_odometro_diario_personas_id_conductor",
                table: "odometro_diario");

            migrationBuilder.DropForeignKey(
                name: "FK_odometro_diario_rutas_id_ruta",
                table: "odometro_diario");

            migrationBuilder.DropForeignKey(
                name: "FK_odometro_diario_vehiculos_id_vehiculo",
                table: "odometro_diario");

            migrationBuilder.DropForeignKey(
                name: "FK_ordenes_mantenimiento_talleres_id_taller",
                table: "ordenes_mantenimiento");

            migrationBuilder.DropForeignKey(
                name: "FK_ordenes_mantenimiento_vehiculos_id_vehiculo",
                table: "ordenes_mantenimiento");

            migrationBuilder.DropForeignKey(
                name: "FK_polizas_seguros_vehiculos_id_vehiculo",
                table: "polizas_seguros");

            migrationBuilder.DropForeignKey(
                name: "FK_salarios_diarios_personas_id_persona",
                table: "salarios_diarios");

            migrationBuilder.DropForeignKey(
                name: "FK_salarios_diarios_vehiculos_id_vehiculo",
                table: "salarios_diarios");

            migrationBuilder.DropForeignKey(
                name: "FK_vehiculos_rutas_id_ruta",
                table: "vehiculos");

            migrationBuilder.DropForeignKey(
                name: "FK_vehiculos_tipos_vehiculo_id_tipo_vehiculo",
                table: "vehiculos");

            migrationBuilder.DropIndex(
                name: "IX_vehiculos_id_ruta",
                table: "vehiculos");

            migrationBuilder.DropIndex(
                name: "IX_vehiculos_id_tipo_vehiculo",
                table: "vehiculos");

            migrationBuilder.DropIndex(
                name: "IX_salarios_diarios_id_persona",
                table: "salarios_diarios");

            migrationBuilder.DropIndex(
                name: "IX_salarios_diarios_id_vehiculo",
                table: "salarios_diarios");

            migrationBuilder.DropIndex(
                name: "IX_polizas_seguros_id_vehiculo",
                table: "polizas_seguros");

            migrationBuilder.DropIndex(
                name: "IX_ordenes_mantenimiento_id_taller",
                table: "ordenes_mantenimiento");

            migrationBuilder.DropIndex(
                name: "IX_ordenes_mantenimiento_id_vehiculo",
                table: "ordenes_mantenimiento");

            migrationBuilder.DropIndex(
                name: "IX_odometro_diario_id_conductor",
                table: "odometro_diario");

            migrationBuilder.DropIndex(
                name: "IX_odometro_diario_id_ruta",
                table: "odometro_diario");

            migrationBuilder.DropIndex(
                name: "IX_odometro_diario_id_vehiculo",
                table: "odometro_diario");

            migrationBuilder.DropIndex(
                name: "IX_gastos_repuestos_id_categoria_repuesto",
                table: "gastos_repuestos");

            migrationBuilder.DropIndex(
                name: "IX_gastos_repuestos_id_vehiculo",
                table: "gastos_repuestos");

            migrationBuilder.DropIndex(
                name: "IX_cargas_combustible_id_conductor",
                table: "cargas_combustible");

            migrationBuilder.DropIndex(
                name: "IX_cargas_combustible_id_vehiculo",
                table: "cargas_combustible");

            migrationBuilder.AddColumn<int>(
                name: "RutaIdRuta",
                table: "vehiculos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TipoVehiculoIdTipoVehiculo",
                table: "vehiculos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PersonaIdPersona",
                table: "salarios_diarios",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VehiculoIdVehiculo",
                table: "salarios_diarios",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VehiculoIdVehiculo",
                table: "polizas_seguros",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TallerIdTaller",
                table: "ordenes_mantenimiento",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VehiculoIdVehiculo",
                table: "ordenes_mantenimiento",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConductorIdPersona",
                table: "odometro_diario",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RutaIdRuta",
                table: "odometro_diario",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VehiculoIdVehiculo",
                table: "odometro_diario",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CategoriaRepuestoIdCategoriaRepuesto",
                table: "gastos_repuestos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VehiculoIdVehiculo",
                table: "gastos_repuestos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConductorIdPersona",
                table: "cargas_combustible",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VehiculoIdVehiculo",
                table: "cargas_combustible",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_vehiculos_RutaIdRuta",
                table: "vehiculos",
                column: "RutaIdRuta");

            migrationBuilder.CreateIndex(
                name: "IX_vehiculos_TipoVehiculoIdTipoVehiculo",
                table: "vehiculos",
                column: "TipoVehiculoIdTipoVehiculo");

            migrationBuilder.CreateIndex(
                name: "IX_salarios_diarios_PersonaIdPersona",
                table: "salarios_diarios",
                column: "PersonaIdPersona");

            migrationBuilder.CreateIndex(
                name: "IX_salarios_diarios_VehiculoIdVehiculo",
                table: "salarios_diarios",
                column: "VehiculoIdVehiculo");

            migrationBuilder.CreateIndex(
                name: "IX_polizas_seguros_VehiculoIdVehiculo",
                table: "polizas_seguros",
                column: "VehiculoIdVehiculo");

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_mantenimiento_TallerIdTaller",
                table: "ordenes_mantenimiento",
                column: "TallerIdTaller");

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_mantenimiento_VehiculoIdVehiculo",
                table: "ordenes_mantenimiento",
                column: "VehiculoIdVehiculo");

            migrationBuilder.CreateIndex(
                name: "IX_odometro_diario_ConductorIdPersona",
                table: "odometro_diario",
                column: "ConductorIdPersona");

            migrationBuilder.CreateIndex(
                name: "IX_odometro_diario_RutaIdRuta",
                table: "odometro_diario",
                column: "RutaIdRuta");

            migrationBuilder.CreateIndex(
                name: "IX_odometro_diario_VehiculoIdVehiculo",
                table: "odometro_diario",
                column: "VehiculoIdVehiculo");

            migrationBuilder.CreateIndex(
                name: "IX_gastos_repuestos_CategoriaRepuestoIdCategoriaRepuesto",
                table: "gastos_repuestos",
                column: "CategoriaRepuestoIdCategoriaRepuesto");

            migrationBuilder.CreateIndex(
                name: "IX_gastos_repuestos_VehiculoIdVehiculo",
                table: "gastos_repuestos",
                column: "VehiculoIdVehiculo");

            migrationBuilder.CreateIndex(
                name: "IX_cargas_combustible_ConductorIdPersona",
                table: "cargas_combustible",
                column: "ConductorIdPersona");

            migrationBuilder.CreateIndex(
                name: "IX_cargas_combustible_VehiculoIdVehiculo",
                table: "cargas_combustible",
                column: "VehiculoIdVehiculo");

            migrationBuilder.AddForeignKey(
                name: "FK_cargas_combustible_personas_ConductorIdPersona",
                table: "cargas_combustible",
                column: "ConductorIdPersona",
                principalTable: "personas",
                principalColumn: "id_persona");

            migrationBuilder.AddForeignKey(
                name: "FK_cargas_combustible_vehiculos_VehiculoIdVehiculo",
                table: "cargas_combustible",
                column: "VehiculoIdVehiculo",
                principalTable: "vehiculos",
                principalColumn: "id_vehiculo");

            migrationBuilder.AddForeignKey(
                name: "FK_gastos_repuestos_categorias_repuesto_CategoriaRepuestoIdCategoriaRepuesto",
                table: "gastos_repuestos",
                column: "CategoriaRepuestoIdCategoriaRepuesto",
                principalTable: "categorias_repuesto",
                principalColumn: "id_categoria_repuesto");

            migrationBuilder.AddForeignKey(
                name: "FK_gastos_repuestos_vehiculos_VehiculoIdVehiculo",
                table: "gastos_repuestos",
                column: "VehiculoIdVehiculo",
                principalTable: "vehiculos",
                principalColumn: "id_vehiculo");

            migrationBuilder.AddForeignKey(
                name: "FK_odometro_diario_personas_ConductorIdPersona",
                table: "odometro_diario",
                column: "ConductorIdPersona",
                principalTable: "personas",
                principalColumn: "id_persona");

            migrationBuilder.AddForeignKey(
                name: "FK_odometro_diario_rutas_RutaIdRuta",
                table: "odometro_diario",
                column: "RutaIdRuta",
                principalTable: "rutas",
                principalColumn: "id_ruta");

            migrationBuilder.AddForeignKey(
                name: "FK_odometro_diario_vehiculos_VehiculoIdVehiculo",
                table: "odometro_diario",
                column: "VehiculoIdVehiculo",
                principalTable: "vehiculos",
                principalColumn: "id_vehiculo");

            migrationBuilder.AddForeignKey(
                name: "FK_ordenes_mantenimiento_talleres_TallerIdTaller",
                table: "ordenes_mantenimiento",
                column: "TallerIdTaller",
                principalTable: "talleres",
                principalColumn: "id_taller");

            migrationBuilder.AddForeignKey(
                name: "FK_ordenes_mantenimiento_vehiculos_VehiculoIdVehiculo",
                table: "ordenes_mantenimiento",
                column: "VehiculoIdVehiculo",
                principalTable: "vehiculos",
                principalColumn: "id_vehiculo");

            migrationBuilder.AddForeignKey(
                name: "FK_polizas_seguros_vehiculos_VehiculoIdVehiculo",
                table: "polizas_seguros",
                column: "VehiculoIdVehiculo",
                principalTable: "vehiculos",
                principalColumn: "id_vehiculo");

            migrationBuilder.AddForeignKey(
                name: "FK_salarios_diarios_personas_PersonaIdPersona",
                table: "salarios_diarios",
                column: "PersonaIdPersona",
                principalTable: "personas",
                principalColumn: "id_persona");

            migrationBuilder.AddForeignKey(
                name: "FK_salarios_diarios_vehiculos_VehiculoIdVehiculo",
                table: "salarios_diarios",
                column: "VehiculoIdVehiculo",
                principalTable: "vehiculos",
                principalColumn: "id_vehiculo");

            migrationBuilder.AddForeignKey(
                name: "FK_vehiculos_rutas_RutaIdRuta",
                table: "vehiculos",
                column: "RutaIdRuta",
                principalTable: "rutas",
                principalColumn: "id_ruta");

            migrationBuilder.AddForeignKey(
                name: "FK_vehiculos_tipos_vehiculo_TipoVehiculoIdTipoVehiculo",
                table: "vehiculos",
                column: "TipoVehiculoIdTipoVehiculo",
                principalTable: "tipos_vehiculo",
                principalColumn: "id_tipo_vehiculo");
        }
    }
}
