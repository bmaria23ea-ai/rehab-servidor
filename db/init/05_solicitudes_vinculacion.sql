-- Migración 5: vinculación de visores sin escribir en las gafas (fase 3).
-- Las gafas piden una solicitud y muestran un código; el administrador lo aprueba en el servidor.
-- Reemplaza a los códigos de enrolamiento escritos en las gafas (migración 3).
-- En una base existente se aplica una vez, a mano, con el usuario root.

DROP TABLE IF EXISTS codigos_enrolamiento;

CREATE TABLE solicitudes_vinculacion (
    id               INT UNSIGNED      NOT NULL AUTO_INCREMENT,
    codigo_hash      BINARY(32)        NOT NULL,                      -- SHA-256 del código que muestran las gafas
    dispositivo_hash BINARY(32)        NOT NULL,                      -- SHA-256 del identificador secreto de la solicitud
    creada_utc       DATETIME(3)       NOT NULL DEFAULT (UTC_TIMESTAMP(3)),
    expira_utc       DATETIME(3)       NOT NULL,
    visor_id         SMALLINT UNSIGNED NULL,                          -- se llena al aprobarla
    PRIMARY KEY (id),
    UNIQUE KEY uq_solicitudes_codigo (codigo_hash),
    UNIQUE KEY uq_solicitudes_dispositivo (dispositivo_hash),
    KEY ix_solicitudes_expira (expira_utc),
    CONSTRAINT fk_solicitudes_visor FOREIGN KEY (visor_id) REFERENCES visores (id) ON DELETE CASCADE,
    CONSTRAINT ck_solicitudes_expira CHECK (expira_utc > creada_utc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO esquema_migraciones (version, descripcion) VALUES (5, 'Vinculación aprobada en el servidor');
