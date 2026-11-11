-- Migración 11: observación del terapeuta por sesión (texto libre, opcional).

CREATE TABLE sesion_observaciones (
    sesion_id       BIGINT UNSIGNED NOT NULL,
    texto           VARCHAR(1000)   NOT NULL,
    terapeuta       VARCHAR(30)     NOT NULL,
    actualizado_utc DATETIME(3)     NOT NULL DEFAULT (UTC_TIMESTAMP(3)),
    PRIMARY KEY (sesion_id),
    CONSTRAINT fk_observacion_sesion FOREIGN KEY (sesion_id) REFERENCES sesiones (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO esquema_migraciones (version, descripcion) VALUES (11, 'Observaciones por sesión');
