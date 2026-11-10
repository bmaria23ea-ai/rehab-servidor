-- Migración 7: cuentas de terapeutas del portal (fase 4).

CREATE TABLE terapeutas (
    id                 SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
    usuario            VARCHAR(30)       NOT NULL,
    password_hash      VARCHAR(255)      NOT NULL,                 -- PBKDF2 de ASP.NET Core Identity
    debe_cambiar       BOOLEAN           NOT NULL DEFAULT TRUE,    -- contraseña temporal
    sello              CHAR(32)          NOT NULL,                 -- cambia = cierra todas sus sesiones
    activo             BOOLEAN           NOT NULL DEFAULT TRUE,
    intentos_fallidos  TINYINT UNSIGNED  NOT NULL DEFAULT 0,
    bloqueado_hasta    DATETIME(3)       NULL,
    creado_utc         DATETIME(3)       NOT NULL DEFAULT (UTC_TIMESTAMP(3)),
    ultimo_acceso_utc  DATETIME(3)       NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_terapeutas_usuario (usuario),
    CONSTRAINT ck_terapeutas_usuario CHECK (usuario REGEXP '^[a-z0-9.]{3,30}$')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO esquema_migraciones (version, descripcion) VALUES (7, 'Terapeutas del portal');
