-- Migración 9: claves de cifrado de las cookies del portal (ASP.NET Core Data Protection).

CREATE TABLE portal_claves (
    id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
    nombre      VARCHAR(100) NOT NULL,
    xml         TEXT         NOT NULL,
    creado_utc  DATETIME(3)  NOT NULL DEFAULT (UTC_TIMESTAMP(3)),
    PRIMARY KEY (id),
    UNIQUE KEY uq_portal_claves_nombre (nombre)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO esquema_migraciones (version, descripcion) VALUES (9, 'Claves de las cookies del portal');
