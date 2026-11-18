-- Vida cotidiana (Lobby): la cabecera sigue en `sesiones`; la duración es fin - inicio.
-- El catálogo de logros se valida en la API.
CREATE TABLE vida_cotidiana_sesiones (
    sesion_id        BIGINT UNSIGNED NOT NULL,
    segundos_activos DECIMAL(5,1)    NOT NULL,
    PRIMARY KEY (sesion_id),
    CONSTRAINT fk_vida_sesion FOREIGN KEY (sesion_id) REFERENCES sesiones (id) ON DELETE CASCADE,
    CONSTRAINT ck_vida_activos CHECK (segundos_activos BETWEEN 0 AND 7200)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE vida_cotidiana_logros (
    sesion_id      BIGINT UNSIGNED   NOT NULL,
    codigo         VARCHAR(32)       NOT NULL,
    cantidad       SMALLINT UNSIGNED NOT NULL,
    primer_segundo DECIMAL(5,1)      NOT NULL,
    PRIMARY KEY (sesion_id, codigo),
    CONSTRAINT fk_vida_logro FOREIGN KEY (sesion_id) REFERENCES vida_cotidiana_sesiones (sesion_id) ON DELETE CASCADE,
    CONSTRAINT ck_vida_logro CHECK (cantidad BETWEEN 1 AND 999 AND primer_segundo BETWEEN 0 AND 7200)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO esquema_migraciones (version, descripcion) VALUES (16, 'Sesiones de vida cotidiana');
