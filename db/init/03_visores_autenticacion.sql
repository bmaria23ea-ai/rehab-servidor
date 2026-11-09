-- Migración 3: autenticación de visores (fase 2).
-- En una instalación nueva se ejecuta sola después de 01 y 02.
-- En una base existente se aplica una vez, a mano, con el usuario root.

-- Registro de migraciones aplicadas
CREATE TABLE IF NOT EXISTS esquema_migraciones (
    version      SMALLINT UNSIGNED NOT NULL,
    descripcion  VARCHAR(100)      NOT NULL,
    aplicada_utc DATETIME(3)       NOT NULL DEFAULT (UTC_TIMESTAMP(3)),
    PRIMARY KEY (version)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT IGNORE INTO esquema_migraciones (version, descripcion) VALUES
    (1, 'Esquema inicial de la caminata'),
    (2, 'Permisos mínimos de la API');

-- Visores: estado "pendiente" hasta que se enrolan, y hash de su secreto
ALTER TABLE visores
    MODIFY COLUMN estado ENUM('pendiente','activo','revocado') NOT NULL DEFAULT 'pendiente',
    ADD COLUMN secreto_hash      BINARY(32)  NULL AFTER estado,          -- SHA-256 del secreto del visor
    ADD COLUMN enrolado_utc      DATETIME(3) NULL AFTER registrado_utc,
    ADD COLUMN ultimo_acceso_utc DATETIME(3) NULL AFTER revocado_utc,
    ADD CONSTRAINT ck_visores_secreto CHECK ((estado = 'activo') = (secreto_hash IS NOT NULL));

-- Códigos de enrolamiento: un solo uso, vencen a los 10 minutos
CREATE TABLE codigos_enrolamiento (
    id          INT UNSIGNED      NOT NULL AUTO_INCREMENT,
    visor_id    SMALLINT UNSIGNED NOT NULL,
    codigo_hash BINARY(32)        NOT NULL,                              -- SHA-256 del código
    creado_utc  DATETIME(3)       NOT NULL DEFAULT (UTC_TIMESTAMP(3)),
    expira_utc  DATETIME(3)       NOT NULL,
    usado_utc   DATETIME(3)       NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_codigos_hash (codigo_hash),
    KEY ix_codigos_visor (visor_id),
    CONSTRAINT fk_codigos_visor FOREIGN KEY (visor_id) REFERENCES visores (id) ON DELETE CASCADE,
    CONSTRAINT ck_codigos_expira CHECK (expira_utc > creado_utc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO esquema_migraciones (version, descripcion) VALUES (3, 'Autenticación de visores');
