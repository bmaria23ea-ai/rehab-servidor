-- Migración 10: registro de auditoría del portal (fase 5). Solo se agregan filas.

CREATE TABLE auditoria (
    id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    creado_utc  DATETIME(3)     NOT NULL DEFAULT (UTC_TIMESTAMP(3)),
    terapeuta   VARCHAR(30)     NOT NULL,              -- usuario, se conserva aunque la cuenta cambie
    accion      VARCHAR(30)     NOT NULL,
    objeto      VARCHAR(40)     NULL,                  -- código de paciente, id de gafas o de sesión
    PRIMARY KEY (id),
    KEY ix_auditoria_fecha (creado_utc),
    CONSTRAINT ck_auditoria_accion CHECK (accion REGEXP '^[a-z_]{3,30}$')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TRIGGER auditoria_sin_cambios BEFORE UPDATE ON auditoria FOR EACH ROW
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'auditoria: solo lectura';

CREATE TRIGGER auditoria_sin_borrado BEFORE DELETE ON auditoria FOR EACH ROW
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'auditoria: solo lectura';

INSERT INTO esquema_migraciones (version, descripcion) VALUES (10, 'Auditoría del portal');
