-- Migración 4: asignación de pacientes a visores (fase 3).
-- El terapeuta asigna un paciente a un visor desde el portal; el visor lo usa en sus sesiones.
-- Si no hay asignación, la sesión se guarda "sin asignar" y se asigna después.
-- En una base existente se aplica una vez, a mano, con el usuario root.

-- Sesiones: el paciente puede quedar vacío hasta que se asigne
ALTER TABLE sesiones
    MODIFY COLUMN paciente_id INT UNSIGNED NULL,
    ADD COLUMN asignada_utc DATETIME(3) NULL AFTER recibido_utc;   -- cuándo se asignó después de recibirla

-- Asignación vigente de cada visor (una por visor)
CREATE TABLE asignaciones (
    visor_id    SMALLINT UNSIGNED NOT NULL,
    paciente_id INT UNSIGNED      NOT NULL,
    creada_utc  DATETIME(3)       NOT NULL DEFAULT (UTC_TIMESTAMP(3)),
    expira_utc  DATETIME(3)       NOT NULL,                          -- vence sola (por defecto a las 4 horas)
    PRIMARY KEY (visor_id),
    KEY ix_asignaciones_paciente (paciente_id),
    CONSTRAINT fk_asignaciones_visor    FOREIGN KEY (visor_id)    REFERENCES visores (id)    ON DELETE CASCADE,
    CONSTRAINT fk_asignaciones_paciente FOREIGN KEY (paciente_id) REFERENCES pacientes (id) ON DELETE CASCADE,
    CONSTRAINT ck_asignaciones_expira   CHECK (expira_utc > creada_utc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO esquema_migraciones (version, descripcion) VALUES (4, 'Asignación de pacientes a visores');
